using System;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using JadeDbClient.Interfaces;

namespace JadeDbClient.Helpers;

/// <summary>
/// Translates a two-parameter JOIN ON lambda (e.g., <c>(t, j) => t.FkId == j.Id</c>)
/// into a parameterised SQL ON clause, qualifying each column reference with its
/// respective table name.
/// </summary>
internal sealed class JoinExpressionVisitor : ExpressionVisitor
{
    private readonly StringBuilder _sql = new();
    private readonly List<IDbDataParameter> _parameters = new();
    private int _paramCounter = 0;
    private readonly Func<string, object, DbType, ParameterDirection, int, IDbDataParameter> _parameterFactory;
    private readonly string _leftAlias;
    private readonly string _rightAlias;
    private readonly string _leftParamName;
    private readonly string _rightParamName;

    internal JoinExpressionVisitor(
        IDatabaseService dbService,
        string leftAlias,
        string rightAlias,
        string leftParamName,
        string rightParamName)
        : this(
            (name, val, type, dir, size) => (dbService ?? throw new ArgumentNullException(nameof(dbService))).GetParameter(name, val, type, dir, size),
            leftAlias,
            rightAlias,
            leftParamName,
            rightParamName)
    {
    }

    internal JoinExpressionVisitor(
        Func<string, object, DbType, ParameterDirection, int, IDbDataParameter> parameterFactory,
        string leftAlias,
        string rightAlias,
        string leftParamName,
        string rightParamName)
    {
        _parameterFactory = parameterFactory ?? throw new ArgumentNullException(nameof(parameterFactory));
        _leftAlias = leftAlias;
        _rightAlias = rightAlias;
        _leftParamName = leftParamName;
        _rightParamName = rightParamName;
    }

    internal (string OnClause, IReadOnlyList<IDbDataParameter> Parameters) Translate(LambdaExpression expression)
    {
        Visit(expression.Body);
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
            _ => throw new NotSupportedException(
                $"Binary operator {node.NodeType} is not supported in a JOIN ON expression.")
        };

        _sql.Append(op);
        Visit(node.Right);
        _sql.Append(')');
        return node;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression is ParameterExpression paramExpr)
        {
            var propInfo = (PropertyInfo)node.Member;
            var columnName = ReflectionHelper.GetColumnName(propInfo);
            var alias = paramExpr.Name == _leftParamName ? _leftAlias : _rightAlias;
            _sql.Append($"{alias}.{columnName}");
            return node;
        }

        // Captured variable
        var value = Expression.Lambda(node).Compile().DynamicInvoke();
        AddParameter(value, node.Type);
        return node;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        AddParameter(node.Value, node.Type);
        return node;
    }

    private void AddParameter(object? value, Type targetType)
    {
        var paramName = $"@jp{_paramCounter++}";
        _sql.Append(paramName);

        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        DbType dbType = DbType.Object;
        if (type == typeof(bool))             dbType = DbType.Boolean;
        else if (type == typeof(byte))        dbType = DbType.Byte;
        else if (type == typeof(short))       dbType = DbType.Int16;
        else if (type == typeof(int))         dbType = DbType.Int32;
        else if (type == typeof(long))        dbType = DbType.Int64;
        else if (type == typeof(float))       dbType = DbType.Single;
        else if (type == typeof(double))      dbType = DbType.Double;
        else if (type == typeof(decimal))     dbType = DbType.Decimal;
        else if (type == typeof(DateTime))    dbType = DbType.DateTime2;
        else if (type == typeof(DateTimeOffset)) dbType = DbType.DateTimeOffset;
        else if (type == typeof(Guid))        dbType = DbType.Guid;
        else if (type == typeof(string))      dbType = DbType.String;
        else if (type == typeof(byte[]))      dbType = DbType.Binary;

        var param = _parameterFactory(paramName, value ?? DBNull.Value, dbType, ParameterDirection.Input, 0);
        _parameters.Add(param);
    }
}
