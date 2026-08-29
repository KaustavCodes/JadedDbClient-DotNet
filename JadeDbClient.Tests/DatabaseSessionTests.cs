using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using JadeDbClient.Attributes;
using JadeDbClient.Enums;
using JadeDbClient.Helpers;
using JadeDbClient.Initialize;
using JadeDbClient.Interfaces;
using JadeDbClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace JadeDbClient.Tests;

public class DatabaseSessionTests
{
    [JadeDbTable("test_table")]
    public class TestEntity
    {
        [JadeDbColumn("id")]
        public int Id { get; set; }

        [JadeDbColumn("name")]
        public string Name { get; set; } = string.Empty;
    }

    private static Mock<IDbDataParameter> CreateMockParameter(string name, object? value, DbType dbType)
    {
        var mock = new Mock<IDbDataParameter>();
        mock.SetupProperty(p => p.ParameterName, name);
        mock.SetupProperty(p => p.Value, value);
        mock.SetupProperty(p => p.DbType, dbType);
        return mock;
    }

    [Fact]
    public void QueryBuilder_CanBeConstructedWithDatabaseSession()
    {
        var sessionMock = new Mock<IDatabaseSession>();
        sessionMock.Setup(s => s.Dialect).Returns(DatabaseDialect.PostgreSql);
        sessionMock.Setup(s => s.PluralizeTableNames).Returns(false);
        sessionMock.Setup(s => s.GetParameter(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<DbType>(), It.IsAny<ParameterDirection>(), It.IsAny<int>()))
            .Returns<string, object, DbType, ParameterDirection, int>((name, val, type, dir, size) => CreateMockParameter(name, val, type).Object);

        var qb = new QueryBuilder<TestEntity>(sessionMock.Object);
        var (sql, prms) = qb.Where(t => t.Id == 42).BuildSelect();

        sql.Should().Be("SELECT id, name FROM test_table WHERE (id = @p0)");
        prms.Should().HaveCount(1);
    }

    [Fact]
    public async Task QueryBuilder_ExecutesViaDatabaseSession()
    {
        var sessionMock = new Mock<IDatabaseSession>();
        sessionMock.Setup(s => s.Dialect).Returns(DatabaseDialect.PostgreSql);
        sessionMock.Setup(s => s.PluralizeTableNames).Returns(false);
        sessionMock.Setup(s => s.GetParameter(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<DbType>(), It.IsAny<ParameterDirection>(), It.IsAny<int>()))
            .Returns<string, object, DbType, ParameterDirection, int>((name, val, type, dir, size) => CreateMockParameter(name, val, type).Object);

        var expectedList = new List<TestEntity> { new() { Id = 1, Name = "Alice" } };
        sessionMock.Setup(s => s.ExecuteQueryAsync<TestEntity>(It.IsAny<string>(), It.IsAny<IEnumerable<IDbDataParameter>>()))
            .ReturnsAsync(expectedList);

        var qb = new QueryBuilder<TestEntity>(sessionMock.Object);
        var result = (await qb.Where(t => t.Id == 1).ToListAsync()).ToList();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Alice");
        sessionMock.Verify(s => s.ExecuteQueryAsync<TestEntity>(It.Is<string>(sql => sql.Contains("WHERE (id = @p0)")), It.IsAny<IEnumerable<IDbDataParameter>>()), Times.Once);
    }

    [Fact]
    public void DatabaseSession_TransactionMethods_ManageUnderlyingTransaction()
    {
        var connMock = new Mock<IDbConnection>();
        var txMock = new Mock<IDbTransaction>();
        connMock.Setup(c => c.State).Returns(ConnectionState.Open);
        connMock.Setup(c => c.BeginTransaction()).Returns(txMock.Object);

        var mapperOptions = new JadeDbMapperOptions();
        var mapper = new Mapper(mapperOptions);

        using var session = new DatabaseSession(
            connMock.Object,
            DatabaseDialect.PostgreSql,
            false,
            mapper,
            mapperOptions,
            null,
            (name, val, type, dir, size) => CreateMockParameter(name, val, type).Object);

        session.Transaction.Should().BeNull();

        var tx = session.BeginTransaction();
        tx.Should().Be(txMock.Object);
        session.Transaction.Should().Be(txMock.Object);

        session.CommitTransaction();
        txMock.Verify(t => t.Commit(), Times.Once);
        session.Transaction.Should().BeNull();
    }

    [Fact]
    public void DatabaseServiceOptions_Lifetime_DefaultsToSingleton_CanBeSetToScoped()
    {
        var services = new ServiceCollection();
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["DatabaseType"]).Returns("PostgreSQL");
        configMock.Setup(c => c["ConnectionStrings:DbConnection"]).Returns("Host=localhost;Database=test;");
        services.AddSingleton(configMock.Object);

        services.AddJadeDbService(serviceOptionsConfigure: options =>
        {
            options.Lifetime = ServiceLifetime.Scoped;
        });

        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseService));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void DatabaseServiceRegistration_DefaultLifetime_IsSingleton()
    {
        var services = new ServiceCollection();
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["DatabaseType"]).Returns("PostgreSQL");
        configMock.Setup(c => c["ConnectionStrings:DbConnection"]).Returns("Host=localhost;Database=test;");
        services.AddSingleton(configMock.Object);

        services.AddJadeDbService();

        var descriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IDatabaseService));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }
}
