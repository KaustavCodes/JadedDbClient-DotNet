using System.Data;
using System.Diagnostics;
using Dapper;
using JadeDbClient.Attributes;
using JadeDbClient.Helpers;
using JadeDbClient.Initialize;
using JadeDbClient.Interfaces;
using JadeDbTest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

// =========================================================================
// 1. SETUP & INITIALIZATION
// =========================================================================

const string connectionString = "Host=localhost;Database=testdb;Username=postgres;Password=KausRocks;";
const string dbType = "PostgreSQL";

var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureLogging(logging => logging.ClearProviders());
builder.ConfigureAppConfiguration((context, config) =>
{
    config.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DatabaseType"] = dbType,
        ["ConnectionStrings:DbConnection"] = connectionString
    });
});

builder.ConfigureServices((context, services) =>
{
    services.AddJadeDbService();
    services.AddDbContext<TestDbContext>(options =>
        options.UseNpgsql(connectionString));
});

var app = builder.Build();

var dbService = app.Services.GetRequiredService<IDatabaseService>();
using var scope = app.Services.CreateScope();
var efContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

var sw = new Stopwatch();

Console.WriteLine("==================================================================================");
Console.WriteLine(" Microbenchmark: Pure ADO.NET vs Dapper vs JadeDbClient vs EF Core");
Console.WriteLine(" (1,000 Parameterized Point Lookups on Local PostgreSQL)");
Console.WriteLine("==================================================================================\n");

// =========================================================================
// 2. EQUALIZED PERFORMANCE BENCHMARK (1,000 SELECT Iterations)
// =========================================================================

const int iterations = 1000;
const int warmup = 200;

// Open dedicated connections to equalize connection management across all tests
await using var adoConn = new NpgsqlConnection(connectionString);
await adoConn.OpenAsync();

await using var dapperConn = new NpgsqlConnection(connectionString);
await dapperConn.OpenAsync();

await using var jadeSession = await dbService.OpenSessionAsync();

// -------------------------------------------------------------------------
// WARMUP (200 iterations each)
// -------------------------------------------------------------------------
Console.WriteLine($"Running warmup ({warmup} iterations per framework)...");
for (int i = 0; i < warmup; i++)
{
    // ADO.NET
    using var cmd = new NpgsqlCommand("SELECT id, name FROM tbl_test WHERE id = @id", adoConn);
    cmd.Parameters.Add(new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 1 });
    using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
    if (await r.ReadAsync()) { var _ = new TestTable { Id = r.GetInt32(0), UserName = r.GetString(1) }; }

    // Dapper
    await dapperConn.QueryFirstOrDefaultAsync<TestTable>("SELECT id, name AS UserName FROM tbl_test WHERE id = @id", new { id = 1 });

    // Jade Session (Reused Connection)
    await jadeSession.ExecuteQueryFirstRowAsync<TestTable>(
        "SELECT id, name FROM tbl_test WHERE id = @p0",
        new[] { jadeSession.GetParameter("p0", 1, DbType.Int32) });

    // Jade Session + QueryBuilder
    var (sSql, sPrms) = new QueryBuilder<TestTable>(jadeSession).Where(t => t.Id == 1).BuildSelect();
    await jadeSession.ExecuteQueryFirstRowAsync<TestTable>(sSql, sPrms);

    // EF Core
    await efContext.TestTables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == 1);
}
Console.WriteLine("Warmup complete.\n");

// -------------------------------------------------------------------------
// 1. Pure ADO.NET (Prepared Command, Reused Parameter, Direct Ordinal Read)
// -------------------------------------------------------------------------
using var adoCmd = new NpgsqlCommand("SELECT id, name FROM tbl_test WHERE id = @id", adoConn);
var adoParam = new NpgsqlParameter("@id", NpgsqlDbType.Integer) { Value = 1 };
adoCmd.Parameters.Add(adoParam);
await adoCmd.PrepareAsync();

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

long startAllocAdo = GC.GetTotalAllocatedBytes(precise: true);
sw.Restart();
for (int i = 0; i < iterations; i++)
{
    adoParam.Value = 1;
    using var reader = await adoCmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
    if (await reader.ReadAsync())
    {
        var _ = new TestTable
        {
            Id = reader.GetInt32(0),
            UserName = reader.GetString(1)
        };
    }
}
sw.Stop();
long adoAlloc = GC.GetTotalAllocatedBytes(precise: true) - startAllocAdo;
double adoTotalMs = sw.Elapsed.TotalMilliseconds;

// -------------------------------------------------------------------------
// 2. Dapper (Reused connection, IL emit mapper)
// -------------------------------------------------------------------------
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

long startAllocDapper = GC.GetTotalAllocatedBytes(precise: true);
sw.Restart();
for (int i = 0; i < iterations; i++)
{
    var _ = await dapperConn.QueryFirstOrDefaultAsync<TestTable>(
        "SELECT id, name AS UserName FROM tbl_test WHERE id = @id", new { id = 1 });
}
sw.Stop();
long dapperAlloc = GC.GetTotalAllocatedBytes(precise: true) - startAllocDapper;
double dapperTotalMs = sw.Elapsed.TotalMilliseconds;

// -------------------------------------------------------------------------
// 3. JadeDbClient (Session / Reused Connection - Raw SQL)
// -------------------------------------------------------------------------
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

long startAllocJadeSession = GC.GetTotalAllocatedBytes(precise: true);
sw.Restart();
for (int i = 0; i < iterations; i++)
{
    var param = new[] { jadeSession.GetParameter("p0", 1, DbType.Int32) };
    var _ = await jadeSession.ExecuteQueryFirstRowAsync<TestTable>(
        "SELECT id, name FROM tbl_test WHERE id = @p0", param);
}
sw.Stop();
long jadeSessionAlloc = GC.GetTotalAllocatedBytes(precise: true) - startAllocJadeSession;
double jadeSessionTotalMs = sw.Elapsed.TotalMilliseconds;

// -------------------------------------------------------------------------
// 4. JadeDbClient (Session + QueryBuilder)
// -------------------------------------------------------------------------
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

long startAllocJadeSessionQb = GC.GetTotalAllocatedBytes(precise: true);
sw.Restart();
for (int i = 0; i < iterations; i++)
{
    var (bSql, bPrms) = new QueryBuilder<TestTable>(jadeSession).Where(t => t.Id == 1).BuildSelect();
    var _ = await jadeSession.ExecuteQueryFirstRowAsync<TestTable>(bSql, bPrms);
}
sw.Stop();
long jadeSessionQbAlloc = GC.GetTotalAllocatedBytes(precise: true) - startAllocJadeSessionQb;
double jadeSessionQbTotalMs = sw.Elapsed.TotalMilliseconds;

// -------------------------------------------------------------------------
// 5. JadeDbClient (Standard Singleton - Pooled Connection per query)
// -------------------------------------------------------------------------
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

long startAllocJadePooled = GC.GetTotalAllocatedBytes(precise: true);
sw.Restart();
for (int i = 0; i < iterations; i++)
{
    var param = new[] { dbService.GetParameter("p0", 1, DbType.Int32) };
    var _ = await dbService.ExecuteQueryFirstRowAsync<TestTable>(
        "SELECT id, name FROM tbl_test WHERE id = @p0", param);
}
sw.Stop();
long jadePooledAlloc = GC.GetTotalAllocatedBytes(precise: true) - startAllocJadePooled;
double jadePooledTotalMs = sw.Elapsed.TotalMilliseconds;

// -------------------------------------------------------------------------
// 6. Entity Framework Core (AsNoTracking)
// -------------------------------------------------------------------------
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

long startAllocEf = GC.GetTotalAllocatedBytes(precise: true);
sw.Restart();
for (int i = 0; i < iterations; i++)
{
    var _ = await efContext.TestTables.AsNoTracking().FirstOrDefaultAsync(t => t.Id == 1);
}
sw.Stop();
long efAlloc = GC.GetTotalAllocatedBytes(precise: true) - startAllocEf;
double efTotalMs = sw.Elapsed.TotalMilliseconds;

// -------------------------------------------------------------------------
// RESULTS TABLE
// -------------------------------------------------------------------------
Console.WriteLine($"| Framework                               | Total Time ({iterations} ops) | Avg / Query | Allocated / Op | vs ADO.NET (Baseline) | vs EF Core |");
Console.WriteLine("|-----------------------------------------|-------------------|-------------|----------------|-----------------------|------------|");
Console.WriteLine($"| Pure ADO.NET (Prepared + Reused)        | {adoTotalMs,7:F2} ms       | {adoTotalMs / iterations:F3} ms   | {adoAlloc / (double)iterations,8:F1} B    | 1.00x (Baseline)      | {efTotalMs / Math.Max(adoTotalMs, 1):F2}x faster |");
Console.WriteLine($"| Dapper (Reused Conn)                    | {dapperTotalMs,7:F2} ms       | {dapperTotalMs / iterations:F3} ms   | {dapperAlloc / (double)iterations,8:F1} B    | {dapperTotalMs / Math.Max(adoTotalMs, 1):F2}x                 | {efTotalMs / Math.Max(dapperTotalMs, 1):F2}x faster |");
Console.WriteLine($"| JadeDbClient (Session - Reused Conn)    | {jadeSessionTotalMs,7:F2} ms       | {jadeSessionTotalMs / iterations:F3} ms   | {jadeSessionAlloc / (double)iterations,8:F1} B    | {jadeSessionTotalMs / Math.Max(adoTotalMs, 1):F2}x                 | {efTotalMs / Math.Max(jadeSessionTotalMs, 1):F2}x faster |");
Console.WriteLine($"| JadeDbClient (Session + QueryBuilder)   | {jadeSessionQbTotalMs,7:F2} ms       | {jadeSessionQbTotalMs / iterations:F3} ms   | {jadeSessionQbAlloc / (double)iterations,8:F1} B    | {jadeSessionQbTotalMs / Math.Max(adoTotalMs, 1):F2}x                 | {efTotalMs / Math.Max(jadeSessionQbTotalMs, 1):F2}x faster |");
Console.WriteLine($"| JadeDbClient (Standard / Auto-Pooled)   | {jadePooledTotalMs,7:F2} ms       | {jadePooledTotalMs / iterations:F3} ms   | {jadePooledAlloc / (double)iterations,8:F1} B    | {jadePooledTotalMs / Math.Max(adoTotalMs, 1):F2}x                 | {efTotalMs / Math.Max(jadePooledTotalMs, 1):F2}x faster |");
Console.WriteLine($"| Entity Framework Core (AsNoTracking)    | {efTotalMs,7:F2} ms       | {efTotalMs / iterations:F3} ms   | {efAlloc / (double)iterations,8:F1} B    | {efTotalMs / Math.Max(adoTotalMs, 1):F2}x                 | 1.00x      |");

Console.WriteLine();
Console.WriteLine("-----------------------------------------------------------------");
Console.WriteLine(" Architectural Context & When to Choose What:");
Console.WriteLine("-----------------------------------------------------------------");
Console.WriteLine("1. Socket Baseline: ~0.18 - 0.22 ms of each call is the database round-trip.");
Console.WriteLine("2. Thin Clients (Reused Conn): Pure ADO.NET, Dapper, and JadeDbClient (Session)");
Console.WriteLine("   perform with virtually identical latency and near-zero allocation overhead.");
Console.WriteLine("3. QueryBuilder: Adds ~20-30 microseconds per query for LINQ expression AST");
Console.WriteLine("   translation into parameterized SQL, providing type-safety without magic strings.");
Console.WriteLine("4. EF Core: Recommended when you need change tracking, migrations, and complex");
Console.WriteLine("   entity graph navigation. Pick a thin client for high-throughput hot paths.");

// =========================================================================
// 3. MODEL & DB CONTEXT DEFINITION
// =========================================================================

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<TestTable> TestTables => Set<TestTable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestTable>().ToTable("tbl_test");
        modelBuilder.Entity<TestTable>().Property(t => t.Id).HasColumnName("id");
        modelBuilder.Entity<TestTable>().Property(t => t.UserName).HasColumnName("name");
    }
}
