using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JadeDbClient.Interfaces;
using JadeDbClient.Enums;
using JadeDbClient.Helpers;

namespace JadeDbClient.Helpers;

public class QueryBuilder<T> where T : class
{
    private readonly IDatabaseService _dbService;
    private readonly DatabaseDialect _dialect;
    private readonly string _tableName;

    private string[]? _selectColumns;
    // True when _selectColumns came from an expression or from the default (all cols);
    // they will be auto-qualified with the main table name when joins are present.
    // False when they came from the raw-string Select() overload; the caller is
    // responsible for providing correctly qualified identifiers.
    private bool _selectColumnsNeedQualification = true;
    private Expression<Func<T, bool>>? _whereExpression;
    private readonly List<(string Column, bool IsDescending)> _orderings = new();
    private int? _limit;
    private int? _skip;
    private readonly List<IDbDataParameter> _parameters = new();
    private readonly List<(string JoinSql, IReadOnlyList<IDbDataParameter> Parameters)> _joins = new();

    public QueryBuilder(IDatabaseService dbService)
    {
        _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
        _dialect = dbService.Dialect;
        _tableName = ReflectionHelper.GetTableName(typeof(T), dbService.PluralizeTableNames);
    }

    // ── Fluent methods ──

    /// <summary>
    /// Selects specific columns by name. Column names are validated to contain only safe
    /// SQL identifier characters. When joins are present the caller is responsible for
    /// table-qualifying any ambiguous column names.
    /// </summary>
    public QueryBuilder<T> Select(params string[] columns)
    {
        foreach (var col in columns)
            ValidateSqlIdentifier(col);
        _selectColumns = columns;
        _selectColumnsNeedQualification = false;
        return this;
    }

    /// <summary>
    /// Selects specific columns using a type-safe LINQ-style expression.
    /// Supports single property access (<c>p => p.Name</c>) and anonymous-type
    /// projections (<c>p => new { p.Name, p.Price }</c>).
    /// Column names are resolved via <see cref="JadeDbColumnAttribute"/> when present.
    /// </summary>
    public QueryBuilder<T> Select<TResult>(Expression<Func<T, TResult>> selector)
    {
        _selectColumns = ExtractColumnsFromSelector(selector);
        _selectColumnsNeedQualification = true;
        return this;
    }

    /// <summary>
    /// Selects specific columns from the main table and one joined table using a
    /// type-safe two-parameter expression.  Each column reference is automatically
    /// qualified with its respective table name.
    /// <para>
    /// Example: <c>.Select((p, c) =&gt; new { p.Name, c.CategoryName })</c>
    /// </para>
    /// Column names are resolved via <see cref="JadeDbColumnAttribute"/> when present.
    /// </summary>
    /// <typeparam name="TJoin">The type of the joined table.</typeparam>
    /// <typeparam name="TResult">The anonymous or concrete result type of the projection.</typeparam>
    /// <param name="selector">A two-parameter lambda projecting columns from both tables.</param>
    public QueryBuilder<T> Select<TJoin, TResult>(Expression<Func<T, TJoin, TResult>> selector)
        where TJoin : class
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        var joinTableName = ReflectionHelper.GetTableName(typeof(TJoin), _dbService.PluralizeTableNames);
        _selectColumns = ExtractColumnsFromJoinSelector(selector, _tableName, joinTableName);
        // Columns are already fully qualified (table.column) – no further qualification needed.
        _selectColumnsNeedQualification = false;
        return this;
    }

    /// <summary>
    /// Selects columns from the main table and any number of joined tables using a fluent
    /// <see cref="JoinColumnSelector"/> builder.  Each <c>.From&lt;TTable&gt;()</c> call appends
    /// one or more fully-qualified <c>table.column</c> references to the SELECT list.
    /// <para>
    /// This overload is the recommended choice when more than one JOIN is present.
    /// </para>
    /// <para>
    /// Example:
    /// <code>
    /// qb.Join&lt;Category&gt;(...)
    ///   .Join&lt;Order&gt;(...)
    ///   .SelectColumns(cols =&gt; cols
    ///       .From&lt;Product&gt;(p =&gt; new { p.Name, p.Price })
    ///       .From&lt;Category&gt;(c =&gt; c.Name)
    ///       .From&lt;Order&gt;(o =&gt; o.Total))
    ///   .BuildSelect();
    /// </code>
    /// </para>
    /// </summary>
    public QueryBuilder<T> SelectColumns(Action<JoinColumnSelector> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var selector = new JoinColumnSelector(_dbService);
        configure(selector);

        if (selector.Columns.Count == 0)
            throw new ArgumentException("SelectColumns requires at least one column.", nameof(configure));

        _selectColumns = selector.Columns.ToArray();
        // Columns are already fully qualified (table.column) – no further qualification needed.
        _selectColumnsNeedQualification = false;
        return this;
    }

    public QueryBuilder<T> Where(Expression<Func<T, bool>> where)
    {
        _whereExpression = where;
        return this;
    }

    // Primary ordering (must be called first)
    public QueryBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (_orderings.Count > 0)
        {
            throw new InvalidOperationException(
                "OrderBy / OrderByDescending must be called before any ThenBy / ThenByDescending. " +
                "Use ThenBy for additional sort criteria.");
        }

        var column = GetColumnFromExpression(keySelector);
        _orderings.Add((column, false));
        return this;
    }

    public QueryBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (_orderings.Count > 0)
        {
            throw new InvalidOperationException(
                "OrderBy / OrderByDescending must be called before any ThenBy / ThenByDescending. " +
                "Use ThenByDescending for additional sort criteria.");
        }

        var column = GetColumnFromExpression(keySelector);
        _orderings.Add((column, true));
        return this;
    }

    // Secondary and subsequent ordering
    public QueryBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (_orderings.Count == 0)
        {
            throw new InvalidOperationException(
                "ThenBy / ThenByDescending can only be used after an initial OrderBy or OrderByDescending.");
        }

        var column = GetColumnFromExpression(keySelector);
        _orderings.Add((column, false));
        return this;
    }

    public QueryBuilder<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (_orderings.Count == 0)
        {
            throw new InvalidOperationException(
                "ThenBy / ThenByDescending can only be used after an initial OrderBy or OrderByDescending.");
        }

        var column = GetColumnFromExpression(keySelector);
        _orderings.Add((column, true));
        return this;
    }

    // Optional legacy string-based OrderBy (with warning)
    [Obsolete("Prefer expression-based OrderBy/ThenBy for type safety and column attribute support.")]
    public QueryBuilder<T> OrderBy(string orderBy)
    {
        Console.WriteLine("[WARNING] Using legacy string-based OrderBy – prefer expression version for safety");
        ValidateSqlIdentifier(orderBy);
        // Simplistic parsing – assumes no DESC/ASC in string
        _orderings.Add((orderBy, false));
        return this;
    }

    public QueryBuilder<T> Take(int limit)
    {
        _limit = limit;
        return this;
    }

    public QueryBuilder<T> Skip(int skip)
    {
        _skip = skip;
        return this;
    }

    // ── JOIN methods ─────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a JOIN clause to the query.
    /// </summary>
    /// <typeparam name="TJoin">The type mapped to the table being joined.</typeparam>
    /// <param name="on">
    /// An expression specifying the ON condition, e.g.
    /// <c>(product, category) => product.CategoryId == category.Id</c>.
    /// Both parameter names must be distinct.
    /// </param>
    /// <param name="joinType">The type of join (default: <see cref="JoinType.Inner"/>).</param>
    public QueryBuilder<T> Join<TJoin>(
        Expression<Func<T, TJoin, bool>> on,
        JoinType joinType = JoinType.Inner) where TJoin : class
    {
        if (on == null) throw new ArgumentNullException(nameof(on));

        var joinTableName = ReflectionHelper.GetTableName(typeof(TJoin), _dbService.PluralizeTableNames);
        var leftParam = on.Parameters[0];
        var rightParam = on.Parameters[1];

        var visitor = new JoinExpressionVisitor(
            _dbService,
            leftAlias: _tableName,
            rightAlias: joinTableName,
            leftParamName: leftParam.Name!,
            rightParamName: rightParam.Name!);

        var (onSql, onParams) = visitor.Translate(on);

        var joinKeyword = joinType switch
        {
            JoinType.Inner => "INNER JOIN",
            JoinType.Left => "LEFT JOIN",
            JoinType.Right => "RIGHT JOIN",
            JoinType.Full => "FULL JOIN",
            _ => throw new ArgumentOutOfRangeException(nameof(joinType))
        };

        _joins.Add(($"{joinKeyword} {joinTableName} ON {onSql}", onParams));
        return this;
    }

    /// <summary>Shorthand for <see cref="Join{TJoin}"/> with <see cref="JoinType.Left"/>.</summary>
    public QueryBuilder<T> LeftJoin<TJoin>(Expression<Func<T, TJoin, bool>> on) where TJoin : class
        => Join(on, JoinType.Left);

    /// <summary>Shorthand for <see cref="Join{TJoin}"/> with <see cref="JoinType.Right"/>.</summary>
    public QueryBuilder<T> RightJoin<TJoin>(Expression<Func<T, TJoin, bool>> on) where TJoin : class
        => Join(on, JoinType.Right);

    /// <summary>Shorthand for <see cref="Join{TJoin}"/> with <see cref="JoinType.Full"/>.</summary>
    public QueryBuilder<T> FullJoin<TJoin>(Expression<Func<T, TJoin, bool>> on) where TJoin : class
        => Join(on, JoinType.Full);

    // ── Build SELECT ──
    public (string Sql, IEnumerable<IDbDataParameter> Parameters) BuildSelect()
    {
        _parameters.Clear();
        var sb = new StringBuilder("SELECT ");

        var props = ReflectionHelper.GetMappableProperties(typeof(T));
        var columns = _selectColumns?.Length > 0
            ? _selectColumns
            : ReflectionHelper.GetColumnNames(props);

        // When joins are present, qualify unqualified column names with the main table
        // name to prevent column-name ambiguity across joined tables.
        sb.Append(string.Join(", ", columns.Select(QualifyColumn)));
        sb.Append(" FROM ").Append(_tableName);

        // Append JOIN clauses and collect their parameters
        foreach (var (joinSql, joinParams) in _joins)
        {
            sb.Append(' ').Append(joinSql);
            _parameters.AddRange(joinParams);
        }

        AppendWhere(sb);

        // Append ORDER BY (supports multiple levels)
        if (_orderings.Count > 0)
        {
            sb.Append(" ORDER BY ");
            var orderParts = _orderings.Select(o =>
                $"{QualifyColumn(o.Column)}{(o.IsDescending ? " DESC" : " ASC")}");
            sb.Append(string.Join(", ", orderParts));
        }

        AppendPaging(sb);

        return (sb.ToString(), _parameters);
    }

    // ── Build INSERT ──
    public (string Sql, IEnumerable<IDbDataParameter> Parameters) BuildInsert(T entity, bool returnIdentity = false)
    {
        _parameters.Clear();
        var props = ReflectionHelper.GetMappableProperties(typeof(T))
            .Where(p => !ReflectionHelper.IsIgnoredOnInsert(p) && p.CanWrite)
            .ToArray();

        var columns = ReflectionHelper.GetColumnNames(props);
        var paramPlaceholders = new List<string>();
        int paramIndex = 0;

        foreach (var prop in props)
        {
            var paramName = $"@p{paramIndex++}";
            paramPlaceholders.Add(paramName);
            var value = prop.GetValue(entity);
            _parameters.Add(_dbService.GetParameter(paramName, value ?? DBNull.Value, InferDbType(prop.PropertyType)));
        }

        var sql = new StringBuilder($"INSERT INTO {_tableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramPlaceholders)})");

        if (returnIdentity)
        {
            var identityCol = ReflectionHelper.GetIdentityColumnName(typeof(T));
            sql.Append(_dialect switch
            {
                DatabaseDialect.PostgreSql => $" RETURNING {identityCol}",
                DatabaseDialect.MsSql => $" OUTPUT INSERTED.{identityCol}",
                DatabaseDialect.MySql => "; SELECT LAST_INSERT_ID()",
                _ => ""
            });
        }

        return (sql.ToString(), _parameters);
    }

    // ── Build UPDATE ──
    public (string Sql, IEnumerable<IDbDataParameter> Parameters) BuildUpdate(T entity)
    {
        if (_whereExpression == null)
            throw new InvalidOperationException("WHERE clause is required for UPDATE operations.");

        _parameters.Clear();
        var props = ReflectionHelper.GetMappableProperties(typeof(T))
            .Where(p => !ReflectionHelper.IsIgnoredOnInsert(p) && p.CanWrite)
            .ToArray();

        var setClauses = new List<string>();
        int paramIndex = 0;

        foreach (var prop in props)
        {
            var paramName = $"@p{paramIndex++}";
            setClauses.Add($"{ReflectionHelper.GetColumnName(prop)} = {paramName}");
            var value = prop.GetValue(entity);
            _parameters.Add(_dbService.GetParameter(paramName, value ?? DBNull.Value, InferDbType(prop.PropertyType)));
        }

        var sql = new StringBuilder($"UPDATE {_tableName} SET {string.Join(", ", setClauses)}");
        AppendWhere(sql);

        return (sql.ToString(), _parameters);
    }

    // ── Build DELETE ── (unchanged)
    public (string Sql, IEnumerable<IDbDataParameter> Parameters) BuildDelete()
    {
        if (_whereExpression == null)
            throw new InvalidOperationException("WHERE clause is required for DELETE operations to prevent accidental full table deletion.");

        _parameters.Clear();
        var sql = new StringBuilder($"DELETE FROM {_tableName}");
        AppendWhere(sql);

        return (sql.ToString(), _parameters);
    }

    // ── Execute SELECT ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds and executes the SELECT query, mapping each row to
    /// <typeparamref name="TResult"/>.
    /// </summary>
    /// <remarks>
    /// Use this overload when the selected columns all belong to a single known
    /// model type.  When the result set comes from a JOIN that spans multiple
    /// tables, prefer <see cref="ToListAsync()"/> to get dynamic rows instead.
    /// <para>
    /// <typeparamref name="TResult"/> must have public properties and a public
    /// parameterless constructor.  Annotating your model with
    /// <c>[JadeDbObject]</c> will use the pre-compiled AOT-safe mapper;
    /// otherwise the library falls back to reflection.
    /// </para>
    /// </remarks>
    public async Task<IEnumerable<TResult>> ToListAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties |
                                    DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    TResult>() where TResult : class
    {
        var (sql, parameters) = BuildSelect();
        return await _dbService.ExecuteQueryAsync<TResult>(sql, parameters);
    }

    /// <summary>
    /// Builds and executes the SELECT query, mapping each row to the main
    /// model type <typeparamref name="T"/>.
    /// Equivalent to <c>ToListAsync&lt;T&gt;()</c>.
    /// </summary>
    public Task<IEnumerable<T>> ToListAsync()
        => ToListAsync<T>();

    /// <summary>
    /// Builds and executes the SELECT query, returning each row as a
    /// <see langword="dynamic"/> object whose properties correspond to the
    /// column names in the result set.
    /// </summary>
    /// <remarks>
    /// This is the recommended overload for JOIN queries that select columns
    /// from multiple tables, where no single model type represents the full
    /// result row.  Each returned object is backed by an
    /// <see cref="System.Dynamic.ExpandoObject"/> so properties can be
    /// accessed by name.
    /// <para>
    /// <b>AOT note:</b> <see cref="System.Dynamic.ExpandoObject"/> is
    /// fully AOT-safe.  Accessing a returned object's properties via the
    /// <see langword="dynamic"/> keyword compiles correctly under Native AOT.
    /// </para>
    /// </remarks>
    public async Task<IEnumerable<dynamic>> ToDynamicListAsync()
    {
        var (sql, parameters) = BuildSelect();
        return await _dbService.ExecuteQueryDynamicAsync(sql, parameters);
    }

    /// <summary>
    /// Builds and executes the SELECT query, mapping the first row to
    /// <typeparamref name="TResult"/>, or returning <c>default</c> when the
    /// result set is empty.
    /// </summary>
    /// <remarks>
    /// The same AOT constraints as <see cref="ToListAsync{TResult}"/> apply.
    /// </remarks>
    public async Task<TResult?> FirstOrDefaultAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties |
                                    DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    TResult>() where TResult : class
    {
        var (sql, parameters) = BuildSelect();
        return await _dbService.ExecuteQueryFirstRowAsync<TResult>(sql, parameters);
    }

    /// <summary>
    /// Builds and executes the SELECT query, mapping the first row to the
    /// main model type <typeparamref name="T"/>, or returning <c>null</c>
    /// when the result set is empty.
    /// Equivalent to <c>FirstOrDefaultAsync&lt;T&gt;()</c>.
    /// </summary>
    public Task<T?> FirstOrDefaultAsync()
        => FirstOrDefaultAsync<T>();

    /// <summary>
    /// Builds and executes the SELECT query, returning the first row as a
    /// <see langword="dynamic"/> object, or <c>null</c> when the result set
    /// is empty.  Backed by <see cref="System.Dynamic.ExpandoObject"/>.
    /// </summary>
    /// <remarks>
    /// Recommended for JOIN queries that do not map to a single model type.
    /// </remarks>
    public async Task<dynamic?> FirstOrDefaultDynamicAsync()
    {
        var (sql, parameters) = BuildSelect();
        return await _dbService.ExecuteQueryFirstRowDynamicAsync(sql, parameters);
    }
    /// <summary>
    /// Builds and executes a <c>SELECT COUNT(*)</c> query, respecting any
    /// <see cref="Where"/> filter and JOIN clauses already configured on this builder.
    /// </summary>
    /// <returns>
    /// The number of rows that match the current filter.  Returns 0 when the
    /// table is empty or no rows satisfy the WHERE condition.
    /// </returns>
    /// <remarks>
    /// The SELECT and ORDER BY / paging clauses that may have been configured are
    /// ignored – only the FROM, JOIN, and WHERE portions are used.
    /// </remarks>
    public async Task<long> CountAsync()
    {
        // Build a fresh parameter list so we don't mutate the shared _parameters list.
        // Clone the current WHERE parameters by running AppendWhere on a throw-away builder.
        var sb = new StringBuilder("SELECT COUNT(*) FROM ").Append(_tableName);

        // Collect JOIN parameters into a temporary list so the count query is self-contained.
        var countParams = new List<IDbDataParameter>();
        foreach (var (joinSql, joinParams) in _joins)
        {
            sb.Append(' ').Append(joinSql);
            countParams.AddRange(joinParams);
        }

        if (_whereExpression != null)
        {
            var tablePrefix = _joins.Count > 0 ? _tableName : null;
            var startParamIndex = countParams.Count(p => p.ParameterName.StartsWith("@p"));
            var visitor = new ExpressionToSqlVisitor<T>(_dbService, tablePrefix, startParamIndex);
            var (whereClause, whereParams) = visitor.Translate(_whereExpression);

            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sb.Append(" WHERE ").Append(whereClause);
                countParams.AddRange(whereParams);
            }
        }

        var result = await _dbService.ExecuteScalar<long>(sb.ToString(), countParams);
        return result;
    }
    private void AppendWhere(StringBuilder sb)
    {
        if (_whereExpression == null) return;

        // When joins are present, qualify WHERE column references with the main table
        // name to avoid ambiguity with same-named columns in joined tables.
        var tablePrefix = _joins.Count > 0 ? _tableName : null;
        var startParamIndex = _parameters.Count(p => p.ParameterName.StartsWith("@p"));
        var visitor = new ExpressionToSqlVisitor<T>(_dbService, tablePrefix, startParamIndex);
        var (whereClause, whereParams) = visitor.Translate(_whereExpression);

        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sb.Append(" WHERE ").Append(whereClause);
            _parameters.AddRange(whereParams);
        }
    }

    private void AppendPaging(StringBuilder sb)
    {
        if (_limit == null && _skip == null) return;

        switch (_dialect)
        {
            case DatabaseDialect.MsSql:
                if (_orderings.Count == 0)
                    throw new InvalidOperationException("At least one ORDER BY clause is required when using Skip/Take with SQL Server.");

                if (_skip.HasValue)
                    sb.Append($" OFFSET {_skip.Value} ROWS");

                if (_limit.HasValue)
                    sb.Append($" FETCH NEXT {_limit.Value} ROWS ONLY");
                break;

            case DatabaseDialect.PostgreSql:
            case DatabaseDialect.MySql:
                if (_limit.HasValue)
                    sb.Append($" LIMIT {_limit.Value}");

                if (_skip.HasValue)
                    sb.Append($" OFFSET {_skip.Value}");
                break;

            default:
                throw new NotSupportedException($"Paging not implemented for dialect {_dialect}");
        }
    }

    private static string GetColumnFromExpression<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        if (keySelector.Body is not MemberExpression memberExpr ||
            memberExpr.Member is not PropertyInfo propInfo)
        {
            throw new ArgumentException(
                "OrderBy/ThenBy expression must be a simple property access " +
                "(e.g. o => o.CreatedAt). Complex expressions are not supported yet.");
        }

        return ReflectionHelper.GetColumnName(propInfo);
    }

    /// <summary>
    /// Qualifies <paramref name="column"/> with the main table name when joins are present
    /// and the column is not already table-qualified (i.e. does not contain a dot) and is
    /// not the wildcard <c>*</c>.
    /// Columns that came from the raw-string <see cref="Select(string[])"/> overload are
    /// left as-is (<see cref="_selectColumnsNeedQualification"/> is false for those).
    /// </summary>
    private string QualifyColumn(string column)
    {
        if (_joins.Count == 0) return column;
        if (column == "*" || column.Contains('.')) return column;
        if (!_selectColumnsNeedQualification) return column;
        return $"{_tableName}.{column}";
    }

    /// <summary>
    /// Extracts database column names from a LINQ-style selector expression.
    /// Supports:
    /// <list type="bullet">
    ///   <item><c>p => p.Name</c> — single property</item>
    ///   <item><c>p => new { p.Name, p.Price }</c> — anonymous-type projection</item>
    /// </list>
    /// Column names are resolved via <see cref="JadeDbColumnAttribute"/> when present.
    /// </summary>
    private static string[] ExtractColumnsFromSelector<TResult>(Expression<Func<T, TResult>> selector)
    {
        var body = selector.Body;

        // Anonymous type projection: p => new { p.Name, p.Price }
        if (body is NewExpression newExpr)
        {
            var cols = new List<string>();
            foreach (var arg in newExpr.Arguments)
            {
                var memberExpr = arg as MemberExpression
                    ?? (arg as UnaryExpression)?.Operand as MemberExpression;

                if (memberExpr?.Expression is ParameterExpression && memberExpr.Member is PropertyInfo prop)
                {
                    cols.Add(ReflectionHelper.GetColumnName(prop));
                }
                else
                {
                    throw new ArgumentException(
                        "Each member in the anonymous type projection must be a simple property " +
                        "access (e.g., p => new { p.Name, p.Price }).",
                        nameof(selector));
                }
            }
            return cols.ToArray();
        }

        // Single property: p => p.Name (possibly boxed via UnaryExpression for value types)
        var single = body as MemberExpression
            ?? (body as UnaryExpression)?.Operand as MemberExpression;

        if (single?.Expression is ParameterExpression && single.Member is PropertyInfo singleProp)
            return new[] { ReflectionHelper.GetColumnName(singleProp) };

        throw new ArgumentException(
            "Selector must be a simple property access (p => p.Name) " +
            "or an anonymous type projection (p => new { p.Name, p.Price }).",
            nameof(selector));
    }

    /// <summary>
    /// Extracts fully-qualified database column names (table.column) from a two-parameter
    /// LINQ-style selector expression spanning the main table and one joined table.
    /// Supports:
    /// <list type="bullet">
    ///   <item><c>(p, c) =&gt; p.Name</c> — single property from either table</item>
    ///   <item><c>(p, c) =&gt; new { p.Name, c.CategoryName }</c> — anonymous-type projection</item>
    /// </list>
    /// Column names are resolved via <see cref="JadeDbColumnAttribute"/> when present.
    /// </summary>
    private static string[] ExtractColumnsFromJoinSelector<TJoin, TResult>(
        Expression<Func<T, TJoin, TResult>> selector,
        string mainTableName,
        string joinTableName)
    {
        var mainParam = selector.Parameters[0];
        var joinParam = selector.Parameters[1];

        string QualifyMember(MemberExpression memberExpr)
        {
            if (memberExpr.Member is not PropertyInfo prop)
                throw new ArgumentException(
                    "Each member in the projection must reference a property.",
                    nameof(selector));

            var tableName = (memberExpr.Expression as ParameterExpression) == mainParam
                ? mainTableName
                : joinTableName;

            return $"{tableName}.{ReflectionHelper.GetColumnName(prop)}";
        }

        var body = selector.Body;

        // Anonymous type projection: (p, c) => new { p.Name, c.CategoryName }
        if (body is NewExpression newExpr)
        {
            var cols = new List<string>();
            foreach (var arg in newExpr.Arguments)
            {
                var memberExpr = arg as MemberExpression
                    ?? (arg as UnaryExpression)?.Operand as MemberExpression;

                if (memberExpr?.Expression is ParameterExpression)
                {
                    cols.Add(QualifyMember(memberExpr));
                }
                else
                {
                    throw new ArgumentException(
                        "Each member in the anonymous type projection must be a simple property " +
                        "access (e.g., (p, c) => new { p.Name, c.CategoryName }).",
                        nameof(selector));
                }
            }
            return cols.ToArray();
        }

        // Single property: (p, c) => p.Name  or  (p, c) => c.CategoryName
        var single = body as MemberExpression
            ?? (body as UnaryExpression)?.Operand as MemberExpression;

        if (single?.Expression is ParameterExpression && single.Member is PropertyInfo)
            return new[] { QualifyMember(single) };

        throw new ArgumentException(
            "Selector must be a simple property access (e.g., (p, c) => p.Name) " +
            "or an anonymous type projection (e.g., (p, c) => new { p.Name, c.CategoryName }).",
            nameof(selector));
    }

    /// <summary>
    /// Safe SQL identifier regex: allows alphanumeric/underscore names, dot-qualified names
    /// (schema.table.column), and names wrapped in standard quoting styles
    /// ([name], `name`, "name"). Also allows * for SELECT *.
    /// Quoted forms use negated character classes so the closing delimiter cannot appear
    /// inside the identifier (e.g., [Col]umn] is rejected).
    /// Rejects input containing SQL meta-characters such as semicolons, dashes, or slashes
    /// that could be used for injection.
    /// </summary>
    private static readonly Regex _safeIdentifierRegex = new(
        "^\\*$|^(\\[[^\\]]+\\]|`[^`]+`|\"[^\"]+\"|\\w+)(\\.\\[[^\\]]+\\]|\\.`[^`]+`|\\.\"[^\"]+\"|\\.\\w+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static void ValidateSqlIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Column name must not be null or empty.", nameof(name));

        if (!_safeIdentifierRegex.IsMatch(name))
            throw new ArgumentException(
                $"Column name '{name}' contains invalid characters. " +
                "Only alphanumeric characters, underscores, dots, and standard quoting ([name], `name`, \"name\") are allowed. " +
                "Use expression-based methods instead of raw strings wherever possible.",
                nameof(name));
    }

    private static DbType InferDbType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type switch
        {
            Type t when t == typeof(bool) => DbType.Boolean,
            Type t when t == typeof(byte) => DbType.Byte,
            Type t when t == typeof(short) => DbType.Int16,
            Type t when t == typeof(int) => DbType.Int32,
            Type t when t == typeof(long) => DbType.Int64,
            Type t when t == typeof(float) => DbType.Single,
            Type t when t == typeof(double) => DbType.Double,
            Type t when t == typeof(decimal) => DbType.Decimal,
            Type t when t == typeof(DateTime) => DbType.DateTime2,
            Type t when t == typeof(Guid) => DbType.Guid,
            Type t when t == typeof(string) => DbType.String,
            Type t when t == typeof(byte[]) => DbType.Binary,
            _ => DbType.Object
        };
    }
}