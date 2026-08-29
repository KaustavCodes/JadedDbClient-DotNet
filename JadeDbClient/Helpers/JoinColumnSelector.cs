using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using JadeDbClient.Interfaces;

namespace JadeDbClient.Helpers;

/// <summary>
/// Fluent builder that accumulates fully-qualified (<c>table.column</c>) column references
/// from any number of tables — the main table and one or more joined tables.
/// Obtain an instance via <see cref="QueryBuilder{T}.SelectColumns"/>.
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
public sealed class JoinColumnSelector
{
    private readonly bool _pluralizeTableNames;
    internal List<string> Columns { get; } = new();

    internal JoinColumnSelector(IDatabaseService dbService)
        : this((dbService ?? throw new ArgumentNullException(nameof(dbService))).PluralizeTableNames)
    {
    }

    internal JoinColumnSelector(bool pluralizeTableNames)
    {
        _pluralizeTableNames = pluralizeTableNames;
    }

    /// <summary>
    /// Adds one or more columns from <typeparamref name="TTable"/> to the SELECT list.
    /// Supports:
    /// <list type="bullet">
    ///   <item>Single property: <c>t =&gt; t.Name</c></item>
    ///   <item>Anonymous-type projection: <c>t =&gt; new { t.Name, t.Price }</c></item>
    /// </list>
    /// Each column is automatically qualified as <c>tableName.columnName</c>.
    /// Column names are resolved via <see cref="JadeDbClient.Attributes.JadeDbColumnAttribute"/> when present.
    /// </summary>
    public JoinColumnSelector From<TTable>(Expression<Func<TTable, object?>> selector) where TTable : class
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        var tableName = ReflectionHelper.GetTableName(typeof(TTable), _pluralizeTableNames);
        var param = selector.Parameters[0];

        string QualifyMember(MemberExpression memberExpr)
        {
            if (memberExpr.Member is not PropertyInfo prop)
                throw new ArgumentException(
                    "Each member in the projection must reference a property.",
                    nameof(selector));

            return $"{tableName}.{ReflectionHelper.GetColumnName(prop)}";
        }

        var body = selector.Body;

        // Anonymous type projection: t => new { t.Name, t.Price }
        if (body is NewExpression newExpr)
        {
            foreach (var arg in newExpr.Arguments)
            {
                var memberExpr = arg as MemberExpression
                    ?? (arg as UnaryExpression)?.Operand as MemberExpression;

                if (memberExpr?.Expression is ParameterExpression pe && ReferenceEquals(pe, param))
                {
                    Columns.Add(QualifyMember(memberExpr));
                }
                else
                {
                    throw new ArgumentException(
                        "Each member in the anonymous type projection must be a simple property " +
                        $"access (e.g., t => new {{ t.Name, t.Price }}).",
                        nameof(selector));
                }
            }
            return this;
        }

        // Single property: t => t.Name (reference type) or t => t.Price (value type, boxed via Convert)
        var single = body as MemberExpression
            ?? (body as UnaryExpression)?.Operand as MemberExpression;

        if (single?.Expression is ParameterExpression singlePe && ReferenceEquals(singlePe, param) && single.Member is PropertyInfo)
        {
            Columns.Add(QualifyMember(single));
            return this;
        }

        throw new ArgumentException(
            "Selector must be a simple property access (t => t.Name) " +
            "or an anonymous type projection (t => new { t.Name, t.Price }).",
            nameof(selector));
    }
}
