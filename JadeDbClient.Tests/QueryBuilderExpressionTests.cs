using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using FluentAssertions;
using JadeDbClient.Attributes;
using JadeDbClient.Enums;
using JadeDbClient.Helpers;
using JadeDbClient.Interfaces;
using Moq;
using Xunit;

namespace JadeDbClient.Tests;

public class QueryBuilderExpressionTests
{
    private static Mock<IDatabaseService> CreateMockService(DatabaseDialect dialect = DatabaseDialect.PostgreSql)
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

    [JadeDbTable("users")]
    public class User
    {
        public int Id { get; set; }

        [JadeDbColumn("user_name")]
        public string UserName { get; set; } = string.Empty;

        [JadeDbColumn("email_address")]
        public string Email { get; set; } = string.Empty;
    }

    [Fact]
    public void Where_WithToLowerOnColumnAndEvaluatedConstant_GeneratesLowerAndEvaluatedParam()
    {
        var db = CreateMockService().Object;
        var qb = new QueryBuilder<User>(db);

        var (sql, parameters) = qb.Where(u => u.UserName.ToLower() == "SAM".ToLower()).BuildSelect();

        sql.Should().Contain("LOWER(user_name) = @p0");
        var paramList = parameters.ToList();
        paramList.Should().HaveCount(1);
        paramList[0].Value.Should().Be("sam");
    }

    [Fact]
    public void Where_WithToUpperOnColumn_GeneratesUpperSql()
    {
        var db = CreateMockService().Object;
        var qb = new QueryBuilder<User>(db);

        var (sql, parameters) = qb.Where(u => u.UserName.ToUpper() == "ADMIN").BuildSelect();

        sql.Should().Contain("UPPER(user_name) = @p0");
        var paramList = parameters.ToList();
        paramList.Should().HaveCount(1);
        paramList[0].Value.Should().Be("ADMIN");
    }

    [Fact]
    public void Where_WithTrimMethods_GeneratesTrimLtrimRtrim()
    {
        var db = CreateMockService().Object;
        var qb = new QueryBuilder<User>(db);

        var (sql, _) = qb.Where(u => u.UserName.Trim() == "sam").BuildSelect();
        sql.Should().Contain("TRIM(user_name) = @p0");

        var qb2 = new QueryBuilder<User>(db);
        var (sql2, _) = qb2.Where(u => u.UserName.TrimStart() == "sam").BuildSelect();
        sql2.Should().Contain("LTRIM(user_name) = @p0");

        var qb3 = new QueryBuilder<User>(db);
        var (sql3, _) = qb3.Where(u => u.UserName.TrimEnd() == "sam").BuildSelect();
        sql3.Should().Contain("RTRIM(user_name) = @p0");
    }

    [Fact]
    public void Where_WithStringLength_GeneratesLengthOrLenBasedOnDialect()
    {
        var pgDb = CreateMockService(DatabaseDialect.PostgreSql).Object;
        var (pgSql, _) = new QueryBuilder<User>(pgDb).Where(u => u.UserName.Length > 5).BuildSelect();
        pgSql.Should().Contain("LENGTH(user_name) > @p0");

        var msDb = CreateMockService(DatabaseDialect.MsSql).Object;
        var (msSql, _) = new QueryBuilder<User>(msDb).Where(u => u.UserName.Length > 5).BuildSelect();
        msSql.Should().Contain("LEN(user_name) > @p0");
    }

    [Fact]
    public void Where_WithToLowerAndContains_GeneratesLowerAndLike()
    {
        var db = CreateMockService(DatabaseDialect.PostgreSql).Object;
        var qb = new QueryBuilder<User>(db);

        var (sql, parameters) = qb.Where(u => u.UserName.ToLower().Contains("sam")).BuildSelect();

        sql.Should().Contain("LOWER(user_name) ILIKE @p0");
        var paramList = parameters.ToList();
        paramList.Should().HaveCount(1);
        paramList[0].Value.Should().Be("%sam%");
    }

    [Fact]
    public void Where_WithMethodCallOnCapturedVariable_EvaluatesInCSharp()
    {
        var db = CreateMockService().Object;
        var qb = new QueryBuilder<User>(db);
        var searchList = new List<string> { "alice", "bob" };

        var (sql, parameters) = qb.Where(u => u.UserName == searchList.First()).BuildSelect();

        sql.Should().Contain("user_name = @p0");
        var paramList = parameters.ToList();
        paramList.Should().HaveCount(1);
        paramList[0].Value.Should().Be("alice");
    }
}
