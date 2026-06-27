using System;
using System.Collections.Generic;
using System.Data;
using FluentAssertions;
using JadeDbClient.Attributes;
using JadeDbClient.Enums;
using JadeDbClient.Helpers;
using JadeDbClient.Interfaces;
using Moq;
using Xunit;

namespace JadeDbClient.Tests;

/// <summary>
/// Tests that verify security hardening and correctness of QueryBuilder and
/// ExpressionToSqlVisitor (LIKE escaping, empty IN, identifier validation).
/// </summary>
public class QueryBuilderSecurityTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Mock<IDatabaseService> CreateMockService(DatabaseDialect dialect = DatabaseDialect.MsSql)
    {
        var mock = new Mock<IDatabaseService>();
        mock.Setup(s => s.Dialect).Returns(dialect);
        mock.Setup(s => s.GetParameter(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<DbType>(),
                It.IsAny<ParameterDirection>(), It.IsAny<int>()))
            .Returns<string, object, DbType, ParameterDirection, int>((name, value, type, dir, size) =>
            {
                var p = new Mock<IDbDataParameter>();
                p.Setup(x => x.ParameterName).Returns(name);
                p.Setup(x => x.Value).Returns(value);
                return p.Object;
            });
        return mock;
    }

    // ── Test model ───────────────────────────────────────────────────────────

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int CategoryId { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════════
    // 1. Select() – SQL identifier validation
    // ════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Name")]
    [InlineData("product_name")]
    [InlineData("schema.TableName")]
    [InlineData("[Name]")]
    [InlineData("`Name`")]
    [InlineData("\"Name\"")]
    [InlineData("*")]
    public void Select_WithValidColumnName_DoesNotThrow(string column)
    {
        var svc = CreateMockService();
        var qb = new QueryBuilder<Product>(svc.Object);

        var act = () => qb.Select(column);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Name; DROP TABLE Products--")]
    [InlineData("Name' OR '1'='1")]
    [InlineData("Name/*comment*/")]
    [InlineData("Name UNION SELECT * FROM Users")]
    [InlineData("")]
    [InlineData("   ")]
    public void Select_WithMaliciousOrEmptyColumnName_ThrowsArgumentException(string column)
    {
        var svc = CreateMockService();
        var qb = new QueryBuilder<Product>(svc.Object);

        var act = () => qb.Select(column);

        act.Should().Throw<ArgumentException>();
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. Legacy OrderBy(string) – SQL identifier validation
    // ════════════════════════════════════════════════════════════════════════

#pragma warning disable CS0618 // obsolete on purpose
    [Theory]
    [InlineData("Name")]
    [InlineData("created_at")]
    [InlineData("[Name]")]
    public void LegacyOrderBy_WithValidColumnName_DoesNotThrow(string column)
    {
        var svc = CreateMockService();
        var qb = new QueryBuilder<Product>(svc.Object);

        var act = () => qb.OrderBy(column);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Name; DROP TABLE Products--")]
    [InlineData("1=1--")]
    [InlineData("")]
    public void LegacyOrderBy_WithMaliciousOrEmptyColumnName_ThrowsArgumentException(string column)
    {
        var svc = CreateMockService();
        var qb = new QueryBuilder<Product>(svc.Object);

        var act = () => qb.OrderBy(column);

        act.Should().Throw<ArgumentException>();
    }
#pragma warning restore CS0618

    // ════════════════════════════════════════════════════════════════════════
    // 3. LIKE wildcard escaping – Contains / StartsWith / EndsWith
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Where_Contains_WithPercentSign_EscapesWildcard()
    {
        var svc = CreateMockService(DatabaseDialect.MsSql);
        var qb = new QueryBuilder<Product>(svc.Object);
        string search = "50%off";

        qb.Where(p => p.Name.Contains(search));
        var (sql, parameters) = qb.BuildSelect();

        // The generated parameter value must NOT contain a raw % (unescaped)
        // It should be wrapped: %50~%off%
        var paramList = new List<IDbDataParameter>(parameters);
        var paramValue = paramList[0].Value as string;
        paramValue.Should().Be("%50~%off%");
        sql.Should().Contain("ESCAPE '~'");
    }

    [Fact]
    public void Where_StartsWith_WithUnderscore_EscapesWildcard()
    {
        var svc = CreateMockService(DatabaseDialect.MsSql);
        var qb = new QueryBuilder<Product>(svc.Object);
        string search = "prod_uct";

        qb.Where(p => p.Name.StartsWith(search));
        var (sql, parameters) = qb.BuildSelect();

        var paramList = new List<IDbDataParameter>(parameters);
        var paramValue = paramList[0].Value as string;
        paramValue.Should().Be("prod~_uct%");
        sql.Should().Contain("ESCAPE '~'");
    }

    [Fact]
    public void Where_EndsWith_WithSqlServerBracket_EscapesBracketForMsSql()
    {
        var svc = CreateMockService(DatabaseDialect.MsSql);
        var qb = new QueryBuilder<Product>(svc.Object);
        string search = "[special]";

        qb.Where(p => p.Name.EndsWith(search));
        var (sql, parameters) = qb.BuildSelect();

        var paramList = new List<IDbDataParameter>(parameters);
        var paramValue = paramList[0].Value as string;
        paramValue.Should().Be("%~[special]");
        sql.Should().Contain("ESCAPE '~'");
    }

    [Fact]
    public void Where_Contains_WithNormalString_DoesNotAddEscapeClause()
    {
        var svc = CreateMockService(DatabaseDialect.MsSql);
        var qb = new QueryBuilder<Product>(svc.Object);

        qb.Where(p => p.Name.Contains("Widget"));
        var (sql, _) = qb.BuildSelect();

        sql.Should().NotContain("ESCAPE");
    }

    [Fact]
    public void Where_Contains_PostgreSql_UsesILikeAndEscapes()
    {
        var svc = CreateMockService(DatabaseDialect.PostgreSql);
        var qb = new QueryBuilder<Product>(svc.Object);
        string search = "50%off";

        qb.Where(p => p.Name.Contains(search));
        var (sql, parameters) = qb.BuildSelect();

        sql.Should().Contain("ILIKE");
        sql.Should().Contain("ESCAPE '~'");
        var paramList = new List<IDbDataParameter>(parameters);
        (paramList[0].Value as string).Should().Be("%50~%off%");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 4. Empty IN() – must produce always-false predicate, not syntax error
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Where_InWithEmptyList_GeneratesAlwaysFalsePredicate()
    {
        var svc = CreateMockService();
        var qb = new QueryBuilder<Product>(svc.Object);
        var emptyIds = new List<int>();

        qb.Where(p => p.CategoryId.In(emptyIds));
        var (sql, parameters) = qb.BuildSelect();

        sql.Should().Contain("1=0");
        sql.Should().NotContain("IN ()");
        new List<IDbDataParameter>(parameters).Should().BeEmpty();
    }

    [Fact]
    public void Where_InWithNonEmptyList_GeneratesInClause()
    {
        var svc = CreateMockService();
        var qb = new QueryBuilder<Product>(svc.Object);
        var ids = new List<int> { 1, 2, 3 };

        qb.Where(p => p.CategoryId.In(ids));
        var (sql, parameters) = qb.BuildSelect();

        sql.Should().Contain(" IN (@p0, @p1, @p2)");
        sql.Should().NotContain("@p0@p1");
        new List<IDbDataParameter>(parameters).Should().HaveCount(3);
    }

    [Fact]
    public void Where_InWithStringArray_GeneratesInClause()
    {
        var svc = CreateMockService();
        var qb = new QueryBuilder<Product>(svc.Object);
        var statuses = new[] { "active", "shipped" };

        qb.Where(p => p.Name.In(statuses));
        var (sql, parameters) = qb.BuildSelect();

        sql.Should().Contain(" IN (@p0, @p1)");
        new List<IDbDataParameter>(parameters).Should().HaveCount(2);
    }
}
