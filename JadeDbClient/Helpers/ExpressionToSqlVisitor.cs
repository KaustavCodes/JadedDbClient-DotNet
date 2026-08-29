using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using JadeDbClient.Attributes;
using JadeDbClient.Enums;
using JadeDbClient.Interfaces;

namespace JadeDbClient.Helpers;

internal class ExpressionToSqlVisitor<T> : ExpressionVisitor
{
    private readonly StringBuilder _sql = new();
    private readonly List<IDbDataParameter> _parameters = new();
    private int _paramCounter = 0;
    private readonly IDatabaseService _dbService;
    private readonly DatabaseDialect _dialect;
    private readonly string? _tablePrefix;

    public ExpressionToSqlVisitor(IDatabaseService dbService, string? tablePrefix = null)
    {
        _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
        _dialect = dbService.Dialect;
        _tablePrefix = tablePrefix;
    }

    public (string WhereClause, IReadOnlyList<IDbDataParameter> Parameters) Translate(Expression<Func<T, bool>>? predicate)
    {
        if (predicate == null)
        {
            return (string.Empty, _parameters.AsReadOnly());
        }

        Visit(predicate.Body);
        return (_sql.ToString().Trim(), _parameters.AsReadOnly());
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        _sql.Append('(');
        Visit(node.Left);

        string op = node.NodeType switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " <> ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThan => " < ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",
            _ => throw new NotSupportedException($"Binary operator {node.NodeType} is not supported")
        };

        _sql.Append(op);
        Visit(node.Right);
        _sql.Append(')');

        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            _sql.Append("NOT (");
            Visit(node.Operand);
            _sql.Append(")");
            return node;
        }

        return base.VisitUnary(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ParameterExpression)
        {
            // Property access: x => x.Status
            var propInfo = (PropertyInfo)node.Member;
            var columnName = ReflectionHelper.GetColumnName(propInfo);
            _sql.Append(_tablePrefix != null ? $"{_tablePrefix}.{columnName}" : columnName);
            return node;
        }

        if (ContainsParameter(node.Expression))
        {
            // E.g., x => x.Name.Length
            if (node.Member.Name == "Length" && node.Expression?.Type == typeof(string))
            {
                var lengthFunc = _dialect == DatabaseDialect.MsSql ? "LEN(" : "LENGTH(";
                _sql.Append(lengthFunc);
                Visit(node.Expression);
                _sql.Append(")");
                return node;
            }
        }

        // Constant or captured value
        var value = GetValueFromExpression(node);
        AddParameter(value, node.Type);
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        AddParameter(node.Value, node.Type);
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        // If the method call does NOT depend on any lambda parameter (e.g. "SAM".ToLower() or myVar.Trim()),
        // evaluate it in C# and pass as a parameterized value.
        if (!ContainsParameter(node))
        {
            var val = GetValueFromExpression(node);
            AddParameter(val, node.Type);
            return node;
        }

        if (node.Method.DeclaringType == typeof(string))
        {
            switch (node.Method.Name)
            {
                case nameof(string.ToLower):
                case nameof(string.ToLowerInvariant):
                    _sql.Append("LOWER(");
                    Visit(node.Object!);
                    _sql.Append(")");
                    return node;

                case nameof(string.ToUpper):
                case nameof(string.ToUpperInvariant):
                    _sql.Append("UPPER(");
                    Visit(node.Object!);
                    _sql.Append(")");
                    return node;

                case nameof(string.Trim):
                    _sql.Append("TRIM(");
                    Visit(node.Object!);
                    _sql.Append(")");
                    return node;

                case nameof(string.TrimStart):
                    _sql.Append("LTRIM(");
                    Visit(node.Object!);
                    _sql.Append(")");
                    return node;

                case nameof(string.TrimEnd):
                    _sql.Append("RTRIM(");
                    Visit(node.Object!);
                    _sql.Append(")");
                    return node;

                case nameof(string.Contains):
                    Visit(node.Object!);
                    _sql.Append(_dialect == DatabaseDialect.PostgreSql ? " ILIKE " : " LIKE ");
                    AddLikeParameter(node.Arguments[0], "%", "%");
                    return node;

                case nameof(string.StartsWith):
                    Visit(node.Object!);
                    _sql.Append(_dialect == DatabaseDialect.PostgreSql ? " ILIKE " : " LIKE ");
                    AddLikeParameter(node.Arguments[0], "", "%");
                    return node;

                case nameof(string.EndsWith):
                    Visit(node.Object!);
                    _sql.Append(_dialect == DatabaseDialect.PostgreSql ? " ILIKE " : " LIKE ");
                    AddLikeParameter(node.Arguments[0], "%", "");
                    return node;

                case nameof(string.Equals):
                    Visit(node.Object!);
                    _sql.Append(" = ");
                    Visit(node.Arguments[0]);
                    return node;
            }
        }

        // Handle null checks (x.Prop == null)
        if (node.Method.Name == "op_Equality" && node.Arguments.Count == 2 && node.Arguments[1] is ConstantExpression constExpr && constExpr.Value == null)
        {
            Visit(node.Arguments[0]);
            _sql.Append(" IS NULL");
            return node;
        }

        if (node.Method.Name == "op_Inequality" && node.Arguments.Count == 2 && node.Arguments[1] is ConstantExpression constExpr2 && constExpr2.Value == null)
        {
            Visit(node.Arguments[0]);
            _sql.Append(" IS NOT NULL");
            return node;
        }

        // Handle custom In extension method
        if (node.Method.Name == nameof(QueryExtensions.In) && node.Method.IsStatic)
        {
            var valuesExpr = node.Arguments[1];
            var rawValues = (IEnumerable)Expression.Lambda(valuesExpr).Compile().DynamicInvoke()!;
            var values = rawValues.Cast<object?>().ToList();

            if (values.Count == 0)
            {
                // Empty IN() is a SQL syntax error; use always-false predicate instead
                _sql.Append("1=0");
                return node;
            }

            Visit(node.Arguments[0]); // the property
            _sql.Append(" IN (");

            var elementType = node.Method.GetGenericArguments()[0];
            var paramNames = new List<string>();
            foreach (var val in values)
            {
                var paramName = $"@p{_paramCounter++}";
                var dbType = InferDbType(elementType);
                var param = _dbService.GetParameter(paramName, val ?? DBNull.Value, dbType);
                _parameters.Add(param);
                paramNames.Add(paramName);
            }

            _sql.Append(string.Join(", ", paramNames));
            _sql.Append(")");
            return node;
        }

        throw new NotSupportedException($"Method call '{node.Method.Name}' is not supported in expressions.");
    }

    private static bool ContainsParameter(Expression? expr)
    {
        if (expr == null) return false;
        var finder = new ParameterFinder();
        finder.Visit(expr);
        return finder.HasParameter;
    }

    private sealed class ParameterFinder : ExpressionVisitor
    {
        public bool HasParameter { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            HasParameter = true;
            return node;
        }
    }

    private string AddParameter(object? value, Type targetType)
    {
        var paramName = $"@p{_paramCounter++}";
        _sql.Append(paramName);

        var dbType = InferDbType(targetType);
        var param = _dbService.GetParameter(paramName, value ?? DBNull.Value, dbType);
        _parameters.Add(param);

        return paramName;
    }

    private void AddLikeParameter(Expression argExpr, string prefix, string suffix)
    {
        var value = (string?)Expression.Lambda(argExpr).Compile().DynamicInvoke();
        if (value == null)
        {
            _sql.Append("NULL"); // rare case
            return;
        }

        // Escape LIKE wildcard characters in the user-supplied value so that
        // special characters are treated as literals and not as pattern wildcards.
        // '~' is used as the escape character (not special in any supported dialect).
        var escapedValue = value
            .Replace("~", "~~")   // escape the escape character itself first
            .Replace("%", "~%")
            .Replace("_", "~_");

        // SQL Server also treats '[' as a wildcard character in LIKE patterns
        if (_dialect == DatabaseDialect.MsSql)
            escapedValue = escapedValue.Replace("[", "~[");

        AddParameter(prefix + escapedValue + suffix, typeof(string));

        // Append the ESCAPE clause only when characters were actually escaped
        if (escapedValue != value)
            _sql.Append(" ESCAPE '~'");
    }

    private static object? GetValueFromExpression(Expression expr)
    {
        return Expression.Lambda(expr).Compile().DynamicInvoke();
    }

    private static DbType InferDbType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(bool)) return DbType.Boolean;
        if (type == typeof(byte)) return DbType.Byte;
        if (type == typeof(sbyte)) return DbType.SByte;
        if (type == typeof(short)) return DbType.Int16;
        if (type == typeof(ushort)) return DbType.UInt16;
        if (type == typeof(int)) return DbType.Int32;
        if (type == typeof(uint)) return DbType.UInt32;
        if (type == typeof(long)) return DbType.Int64;
        if (type == typeof(ulong)) return DbType.UInt64;
        if (type == typeof(float)) return DbType.Single;
        if (type == typeof(double)) return DbType.Double;
        if (type == typeof(decimal)) return DbType.Decimal;
        if (type == typeof(DateTime)) return DbType.DateTime2;
        if (type == typeof(DateTimeOffset)) return DbType.DateTimeOffset;
        if (type == typeof(Guid)) return DbType.Guid;
        if (type == typeof(string)) return DbType.String;
        if (type == typeof(byte[])) return DbType.Binary;

        return DbType.Object;
    }
}

// Optional extension method for IN clause
public static class QueryExtensions
{
    public static bool In<TValue>(this TValue value, IEnumerable<TValue> allowedValues)
    {
        return allowedValues.Contains(value);
    }
}