# JadeDbClient

[![.NET 8](https://img.shields.io/badge/.NET-8-blue.svg)]([https://aka.ms/new-console-template](https://aka.ms/new-console-template))
[![.NET 9](https://img.shields.io/badge/.NET-9-blue.svg)]([https://aka.ms/new-console-template](https://aka.ms/new-console-template))
[![.NET 10](https://img.shields.io/badge/.NET-10-blue.svg)]([https://aka.ms/new-console-template](https://aka.ms/new-console-template))
[![Nuget](https://img.shields.io/nuget/v/JadeDbClient.svg)](https://www.nuget.org/packages/JadeDbClient)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**JadeDbClient** is a versatile and efficient .NET NuGet package designed to simplify database connections and query execution across multiple database systems: **MySQL**, **SQL Server**, and **PostgreSQL**. It provides common methods to execute queries and stored procedures, making database switching seamless and eliminating the hassle of managing different database clients.

## Features

- **Multi-database Support**: Effortlessly connect to MySQL, SQL Server, and PostgreSQL.
- **Streamlined Query Execution**: Perform queries with ease using a common interface, regardless of the database system.
- **Stored Procedure Support**: Execute stored procedures across different databases without rewriting code.
- **Transaction Support**: Full support for database transactions with commit and rollback capabilities across all database types.
- **🚀 Source Generator for AOT**: Automatically generates optimized mappers at compile-time with the `[JadeDbObject]` attribute - no manual registration needed!
- **Custom Column Mapping**: Use `[JadeDbColumn]` attribute to map database column names (e.g., snake_case) to C# property names (e.g., PascalCase).
- **Exclude Columns from INSERT/UPDATE**: Mark database-managed columns (e.g. IDENTITY, AUTO_INCREMENT) with `[JadeDbColumn(IgnoreOnInsert = true)]` to automatically exclude them from INSERT and UPDATE statements.
- **Identity Column for Returned Keys**: Mark the primary-key / identity column with `[JadeDbColumn(IsIdentity = true)]` so `BuildInsert(returnIdentity: true)` uses the correct column name in the database-specific RETURNING / OUTPUT clause. Falls back to `id` when no property is marked.
- **Custom Table Mapping**: Use `[JadeDbTable]` attribute to map a C# class to a custom database table name.
- **Native AOT Compatible**: Designed for .NET Native AOT applications with compile-time code generation (Note: Underlying database drivers may still have AOT limitations).
- **Consistent API**: Provides a unified API to eliminate the headaches of switching databases.
- **⚠️ Query Builder *(Beta)***: Fluent, type-safe SELECT / INSERT / UPDATE / DELETE / COUNT query construction — see [beta notice](#-query-builder-beta) below before using in production.

## Installation

Install the package via NuGet:

```bash
dotnet add package JadeDbClient
```

Or use the NuGet Package Manager:

```
Install-Package JadeDbClient
```

## Usage

Before we begin we need to let the plugin know what database we are using and where the plugin needs to connect to.

To do this we need to add the following to the web.config or appsettings.json file.

***Important:** Remember to change the connections string as per your database.*

### For MySql Database
```
"DatabaseType": "MySql",
"ConnectionStrings": {
    "DbConnection": "Server=localhost;Port=8889;Database=[Datase Name];User Id=[DB User Name];Password=[Db Password];"
}
```

### For Microsoft SqlServer Database
```
"DatabaseType": "MsSql",
"ConnectionStrings": {
    "DbConnection": "Server=localhost;Database=TestingDb;User Id=[DB User Name];Password=[Db Password];TrustServerCertificate=True;"
}
```

### For PostgreSql Database
```
"DatabaseType": "PostgreSQL",
"ConnectionStrings": {
    "DbConnection": "Host=localhost;Database=TestingDb;Username=[DB User Name];Password=[Db Password];SearchPath=KausCoder;"
}
```

Next we need to load the plugin on application start. We can do this in the **Program.cs** file


We need these 2 lines

### Add the using statement

```
using JadeDbClient.Initialize;
```

### Basic Initialization (Standard Approach)

Initialize the plugin without any custom configuration. The library will use reflection-based mapping automatically:

```csharp
// Call the method to add the database service
builder.Services.AddJadeDbService();
```

**This is the standard approach that works for all .NET applications. Your existing code will continue to work without any changes.**

### Configuration Options

JadeDbClient supports optional logging configuration for development and debugging:

```csharp
builder.Services.AddJadeDbService(
    configure: null, // Mapper configuration (optional)
    serviceOptionsConfigure: options =>
    {
        options.EnableLogging = true;      // Enable timing logs (default: false)
        options.LogExecutedQuery = true;   // Log SQL queries (default: false)
    });
```

**⚠️ Important:** Logging is **disabled by default** for production performance. Enable only during development.

**Backward Compatibility:** Existing code without logging configuration continues to work without any changes.

---

### Initialization in Console Applications (.NET 6+)

For .NET console applications (using top-level statements), you can use `Host.CreateDefaultBuilder` (via the `Microsoft.Extensions.Hosting` package) to set up configuration and initialize `JadeDbClient`:

```csharp
using JadeDbClient.Helpers;
using JadeDbClient.Initialize;
using JadeDbClient.Interfaces;
using JadeDbTest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 1. Create the generic host builder
var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureAppConfiguration((context, config) =>
{
    // Provide settings directly in code (or use appsettings.json)
    config.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DatabaseType"] = "PostgreSQL",
        ["ConnectionStrings:DbConnection"] = "Host=[Host Name];Database=[Database Name];Username=[DB User Name];Password=[Db Password];"
    });
});

// 2. Register your services
builder.ConfigureServices((context, services) =>
{
    // Add JadeDbService (It automatically reads from configuration)
    services.AddJadeDbService();
});

// 3. Build the app
var app = builder.Build();

// 4. Request the database service directly and use it!
var db = app.Services.GetRequiredService<IDatabaseService>();

var qb = new QueryBuilder<TestTable>(db);
var (sql, parameter) = qb.Where(t => t.UserName == "sam").BuildSelect();

var data = await db.ExecuteQueryAsync<TestTable>(sql, parameter);

if (data != null)
{
    foreach (var item in data)
    {
        Console.WriteLine($"Name is {item.UserName}");
    }
}

```

---

### Multiple Database Connections

If your application needs to connect to **more than one database** at the same time, use `AddJadeDbNamedConnections` instead of `AddJadeDbService`.

> **⚠️ Never hardcode connection strings in source code.** Always keep them in `appsettings.json`, environment variables, or a secrets manager (e.g. Azure Key Vault, AWS Secrets Manager).

**When to use each method:**

| Scenario | Recommended method |
|---|---|
| Single database, one connection | `AddJadeDbService()` *(unchanged)* |
| Multiple connections, different DB types | `AddJadeDbNamedConnections()` |
| Multiple connections, same DB type | `AddJadeDbNamedConnections()` |

---

#### How it works

Define all your connections in `appsettings.json` under `JadeDb:Connections`. **The key you give each connection (e.g. `"main"`, `"reports"`) is exactly the name you use in your code** — no extra mapping needed.

Optionally set `JadeDb:DefaultConnection` to the name of whichever connection should be available for direct `IDatabaseService` injection.

`appsettings.json` (placeholder values only — use real credentials via environment variables or a secrets manager):

```json
{
  "JadeDb": {
    "DefaultConnection": "main",
    "Connections": {
      "main": {
        "DatabaseType":   "PostgreSQL",
        "ConnectionString": "Host=localhost;Database=myapp;Username=app;Password=YOUR_PASSWORD;"
      },
      "reports": {
        "DatabaseType":   "PostgreSQL",
        "ConnectionString": "Host=localhost;Database=myapp_reports;Username=app;Password=YOUR_PASSWORD;"
      }
    }
  }
}
```

```csharp
// Program.cs — zero connection strings in code
using JadeDbClient.Initialize;

builder.Services.AddJadeDbNamedConnections();
```

That's it. Because `"main"` and `"reports"` are already the keys in `JadeDb:Connections`, they are ready to use by name immediately — no additional registration or mapping required.

**Keeping secrets out of source control** — in production, override `ConnectionString` values using environment variables (ASP.NET Core reads them automatically):

```bash
# Environment variable format: double-underscore (__) replaces the colon separator
JadeDb__Connections__main__ConnectionString="Host=prod-db;Database=myapp;Username=app;Password=REAL_SECRET;"
JadeDb__Connections__reports__ConnectionString="Host=prod-db;Database=myapp_reports;Username=app;Password=REAL_SECRET;"
```

Or use [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) during development and a secrets manager (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) in production.

---

#### Scenario A — Multiple connections, different DB types

`appsettings.json`:

```json
{
  "JadeDb": {
    "DefaultConnection": "main",
    "Connections": {
      "main": {
        "DatabaseType":   "MsSql",
        "ConnectionString": "Server=main-db;Database=App;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
      },
      "reports": {
        "DatabaseType":   "PostgreSQL",
        "ConnectionString": "Host=reports-db;Database=Reports;Username=app;Password=YOUR_PASSWORD;"
      },
      "analytics": {
        "DatabaseType":   "MySql",
        "ConnectionString": "Server=analytics-db;Database=Analytics;User=app;Password=YOUR_PASSWORD;"
      }
    }
  }
}
```

```csharp
// Program.cs
builder.Services.AddJadeDbNamedConnections(
    serviceOptionsConfigure: options =>
    {
        options.EnableLogging    = true;   // log query timing (default: false)
        options.LogExecutedQuery = true;   // log executed SQL  (default: false)
    });
```

---

#### Scenario B — Multiple connections, same DB type

`appsettings.json`:

```json
{
  "JadeDb": {
    "DefaultConnection": "primary",
    "Connections": {
      "primary": {
        "DatabaseType":   "MsSql",
        "ConnectionString": "Server=primary-db;Database=App;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
      },
      "secondary": {
        "DatabaseType":   "MsSql",
        "ConnectionString": "Server=secondary-db;Database=App;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
      }
    }
  }
}
```

```csharp
// Program.cs
builder.Services.AddJadeDbNamedConnections();
```

---

#### Full example — all options together

`appsettings.json`:

```json
{
  "JadeDb": {
    "DefaultConnection": "main",
    "Connections": {
      "main": {
        "DatabaseType":   "MsSql",
        "ConnectionString": "Server=main-db;Database=App;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
      },
      "reports": {
        "DatabaseType":   "PostgreSQL",
        "ConnectionString": "Host=reports-db;Database=Reports;Username=app;Password=YOUR_PASSWORD;"
      },
      "analytics": {
        "DatabaseType":   "MySql",
        "ConnectionString": "Server=analytics-db;Database=Analytics;User=app;Password=YOUR_PASSWORD;"
      }
    }
  }
}
```

```csharp
// Program.cs
builder.Services.AddJadeDbNamedConnections(
    mapperConfigure: options =>
    {
        // Only needed for third-party models you cannot decorate with [JadeDbObject]
        options.RegisterMapper<ThirdPartyModel>(reader => new ThirdPartyModel
        {
            Id   = reader.GetInt32(reader.GetOrdinal("Id")),
            Name = reader.GetString(reader.GetOrdinal("Name"))
        });
    },
    serviceOptionsConfigure: options =>
    {
        options.EnableLogging    = true;   // log query timing (default: false)
        options.LogExecutedQuery = true;   // log executed SQL  (default: false)
    });
```

---

#### Using named connections

The connection names you defined as keys in `JadeDb:Connections` are used directly in code.

**Inject the default connection directly — no factory, no name needed:**

```csharp
using JadeDbClient.Interfaces;

public class OrderService
{
    private readonly IDatabaseService _db;

    // IDatabaseService resolves to the default connection ("main")
    public OrderService(IDatabaseService db) => _db = db;

    public async Task<IEnumerable<Order>> GetOrdersAsync()
        => await _db.ExecuteQueryAsync<Order>("SELECT * FROM Orders");
}
```

**Resolve any connection by name via `IJadeDbServiceFactory`:**

```csharp
using JadeDbClient.Interfaces;

public class ReportService
{
    private readonly IDatabaseService _mainDb;
    private readonly IDatabaseService _reportsDb;

    public ReportService(IJadeDbServiceFactory dbFactory)
    {
        _mainDb    = dbFactory.GetService();          // returns the default ("main")
        _reportsDb = dbFactory.GetService("reports"); // the "reports" key from appsettings
    }

    public async Task<IEnumerable<ReportSummary>> GetSummaryAsync()
        => await _reportsDb.ExecuteQueryAsync<ReportSummary>("SELECT * FROM SalesSummary");
}
```

**Using both in a single class:**

```csharp
public class SyncService
{
    private readonly IDatabaseService _main;
    private readonly IDatabaseService _analytics;

    public SyncService(IDatabaseService main, IJadeDbServiceFactory factory)
    {
        _main      = main;                              // the default, injected directly
        _analytics = factory.GetService("analytics");  // the "analytics" key from appsettings
    }
}
```

> **Note:** `JadeDb:DefaultConnection` is **optional**. If you do not set it, `IDatabaseService` is **not** registered in DI and `factory.GetService()` *(no-arg)* will throw. Always resolve by name via `IJadeDbServiceFactory` in that case.

---

#### Migrating existing code to use multiple connections

If you already have working code that injects `IDatabaseService` directly, here is exactly how to adapt it for two (or more) named connections.

**Step 1 — update `appsettings.json`**

Replace the old single-connection keys with the `JadeDb:Connections` block. The key names become the names you use in code:

```json
{
  "JadeDb": {
    "DefaultConnection": "main",
    "Connections": {
      "main": {
        "DatabaseType":   "PostgreSQL",
        "ConnectionString": "Host=localhost;Database=jadedbtesting;Username=postgres;Password=YOUR_PASSWORD;"
      },
      "reports": {
        "DatabaseType":   "PostgreSQL",
        "ConnectionString": "Host=localhost;Database=jadedbtesting2;Username=postgres;Password=YOUR_PASSWORD;"
      }
    }
  }
}
```

**Step 2 — update `Program.cs`**

```csharp
// Before
builder.Services.AddJadeDbService();

// After
builder.Services.AddJadeDbNamedConnections();
```

> **Backward compatibility:** Because `"main"` is set as `DefaultConnection`, all existing code that injects `IDatabaseService` directly continues to work without any modification — it will automatically receive the `"main"` connection.

**Step 3 — update your endpoints / controllers**

*Minimal API — using only the default connection (no change needed if `DefaultConnection` is set):*

```csharp
// Before — and still works after, because "main" is the default
app.MapGet("/test-postgres", async (IDatabaseService dbConfig) =>
{
    var dbDataParameters = new List<IDbDataParameter>();
    dbDataParameters.Add(dbConfig.GetParameter("p_name",        "PostgresUser", DbType.String, ParameterDirection.Input,  250));
    dbDataParameters.Add(dbConfig.GetParameter("p_outputparam", "test",         DbType.String, ParameterDirection.Output, 250));

    Dictionary<string, object> outputParameters =
        await dbConfig.ExecuteStoredProcedureWithOutputAsync("your_stored_procedure", dbDataParameters);

    return Results.Ok(outputParameters);
});
```

*Minimal API — using a specific named connection:*

```csharp
// Inject IJadeDbServiceFactory instead, then resolve the connection you need by name
app.MapGet("/test-reports", async (IJadeDbServiceFactory dbFactory) =>
{
    var dbConfig = dbFactory.GetService("reports"); // resolves the "reports" connection

    var dbDataParameters = new List<IDbDataParameter>();
    dbDataParameters.Add(dbConfig.GetParameter("p_name",        "ReportsUser", DbType.String, ParameterDirection.Input,  250));
    dbDataParameters.Add(dbConfig.GetParameter("p_outputparam", "test",        DbType.String, ParameterDirection.Output, 250));

    Dictionary<string, object> outputParameters =
        await dbConfig.ExecuteStoredProcedureWithOutputAsync("your_stored_procedure", dbDataParameters);

    return Results.Ok(outputParameters);
});
```

*Minimal API — using both connections in one handler:*

```csharp
app.MapGet("/test-both", async (IJadeDbServiceFactory dbFactory) =>
{
    var mainDb    = dbFactory.GetService("main");    // or dbFactory.GetService() for the default
    var reportsDb = dbFactory.GetService("reports");

    // Use mainDb for the primary database
    var mainParams = new List<IDbDataParameter>
    {
        mainDb.GetParameter("p_name", "MainUser", DbType.String, ParameterDirection.Input, 250)
    };
    var mainResults = await mainDb.ExecuteQueryAsync<MyModel>("SELECT * FROM my_table", mainParams);

    // Use reportsDb for the reports database
    var reportParams = new List<IDbDataParameter>
    {
        reportsDb.GetParameter("p_name", "ReportUser", DbType.String, ParameterDirection.Input, 250)
    };
    var reportResults = await reportsDb.ExecuteQueryAsync<ReportModel>("SELECT * FROM report_table", reportParams);

    return Results.Ok(new { main = mainResults, reports = reportResults });
});
```

*Controller — same pattern via constructor injection:*

```csharp
using JadeDbClient.Interfaces;

public class TestController : ControllerBase
{
    private readonly IDatabaseService _main;      // injected directly — the default connection
    private readonly IDatabaseService _reports;   // resolved from the factory by name

    public TestController(IDatabaseService main, IJadeDbServiceFactory dbFactory)
    {
        _main    = main;
        _reports = dbFactory.GetService("reports");
    }

    [HttpGet("test-postgres")]
    public async Task<IActionResult> TestPostgres()
    {
        var dbDataParameters = new List<IDbDataParameter>();
        dbDataParameters.Add(_main.GetParameter("p_name",        "PostgresUser", DbType.String, ParameterDirection.Input,  250));
        dbDataParameters.Add(_main.GetParameter("p_outputparam", "test",         DbType.String, ParameterDirection.Output, 250));

        var outputParameters = await _main.ExecuteStoredProcedureWithOutputAsync("your_stored_procedure", dbDataParameters);
        return Ok(outputParameters);
    }

    [HttpGet("test-reports")]
    public async Task<IActionResult> TestReports()
    {
        var dbDataParameters = new List<IDbDataParameter>();
        dbDataParameters.Add(_reports.GetParameter("p_name",        "ReportsUser", DbType.String, ParameterDirection.Input,  250));
        dbDataParameters.Add(_reports.GetParameter("p_outputparam", "test",        DbType.String, ParameterDirection.Output, 250));

        var outputParameters = await _reports.ExecuteStoredProcedureWithOutputAsync("your_stored_procedure", dbDataParameters);
        return Ok(outputParameters);
    }
}
```

> **Summary:** The only change to existing endpoint/controller code is the injection point. Replace `IDatabaseService dbConfig` with `IJadeDbServiceFactory dbFactory`, then call `dbFactory.GetService("connectionName")` to get the service for the database you need. Everything else — `GetParameter`, `ExecuteQueryAsync`, `ExecuteStoredProcedureWithOutputAsync`, etc. — stays exactly the same.

---

### Advanced: AOT-Compatible Mappers with Source Generator

> **✨ Recommended Approach** 🎉  
> JadeDbClient now includes a **Source Generator** that automatically creates optimized mappers at compile-time. Simply decorate your models with `[JadeDbObject]` and the mappers are generated for you!

#### The Modern Way: Using `[JadeDbObject]` Attribute

**For your own models**, you no longer need to write manual `RegisterMapper` calls. Just mark your models as `public partial` and add the `[JadeDbObject]` attribute:

```csharp
using JadeDbClient.Attributes;

[JadeDbObject]
public partial class User
{
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

[JadeDbObject]
public partial class Order
{
    public int OrderId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}
```

That's it! The Source Generator automatically creates optimized mappers for these classes at compile-time using a `[ModuleInitializer]`, so they're available immediately when your application starts.

#### Simplified Setup

```csharp
using JadeDbClient.Initialize;

var builder = WebApplication.CreateBuilder(args);

// That's all you need! The Source Generator automatically registers all [JadeDbObject] models
builder.Services.AddJadeDbService();

var app = builder.Build();
```

#### Why Use the Source Generator?

**Advantages of `[JadeDbObject]`:**
- ✅ **Zero Boilerplate**: No manual mapper registration needed
- ✅ **.NET Native AOT Support**: Mappers generated at compile-time (works in our testing with trimming warnings)
- ✅ **Better Performance**: No reflection overhead
- ✅ **Compile-Time Type Safety**: Errors caught during compilation
- ✅ **Automatic Null Handling**: Supports nullable types (`int?`, `DateTime?`, `string?`)
- ✅ **Auto-Registration**: Uses `[ModuleInitializer]` for zero-config setup

**When to use each approach:**
- ✅ **Use `[JadeDbObject]`**: For all your own models (recommended for AOT)
- ✅ **Use `RegisterMapper`**: Only for third-party models you cannot modify
- ✅ **Normal JIT Build**: For standard .NET applications with reflection support

#### Manual Registration (Third-Party Models Only)

If you need to map a third-party model that you cannot modify with `[JadeDbObject]`, you can still register mappers manually:

```csharp
builder.Services.AddJadeDbService(options =>
{
    // Only needed for third-party models you cannot modify
    options.RegisterMapper<ThirdPartyModel>(reader => new ThirdPartyModel
    {
        Id = reader.GetInt32(reader.GetOrdinal("Id")),
        Name = reader.GetString(reader.GetOrdinal("Name"))
    });
});
```

#### Null Safety Example

The Source Generator automatically handles nullable types:

```csharp
[JadeDbObject]
public partial class Product
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? Stock { get; set; }           // Nullable int
    public DateTime? LastUpdated { get; set; } // Nullable DateTime
    public string? Description { get; set; }   // Nullable string
}
```

When the database returns `DBNull`, the generated mapper assigns `null` for nullable types and appropriate defaults for non-nullable types.

#### Custom Column Name Mapping with `[JadeDbColumn]`

Sometimes your database column names don't match your C# property names (e.g., snake_case in database vs PascalCase in C#). Use the `[JadeDbColumn]` attribute to map them:

```csharp
using JadeDbClient.Attributes;

[JadeDbObject]
public partial class User
{
    [JadeDbColumn("user_id")]
    public int UserId { get; set; }
    
    [JadeDbColumn("user_name")]
    public string UserName { get; set; } = string.Empty;
    
    [JadeDbColumn("email_address")]
    public string EmailAddress { get; set; } = string.Empty;
    
    [JadeDbColumn("is_active")]
    public bool IsActive { get; set; }
    
    [JadeDbColumn("created_at")]
    public DateTime CreatedAt { get; set; }
}
```

**Real-world example:**
```sql
-- Database table with snake_case columns
CREATE TABLE users (
    user_id INT PRIMARY KEY,
    user_name VARCHAR(100),
    email_address VARCHAR(255),
    is_active BOOLEAN,
    created_at TIMESTAMP
);
```

The `[JadeDbColumn]` attribute works in:
- ✅ **Source Generator Mappers**: Column names used in generated `GetOrdinal()` calls
- ✅ **Bulk Insert Operations**: Correct column names in INSERT statements
- ✅ **Reflection Fallback**: Cached lookups for performance

**Mixed Mapping Example:**
```csharp
[JadeDbObject]
public partial class Product
{
    [JadeDbColumn("product_id")]
    public int ProductId { get; set; }
    
    // No attribute - uses property name "ProductName"
    public string ProductName { get; set; } = string.Empty;
    
    [JadeDbColumn("unit_price")]
    public decimal UnitPrice { get; set; }
}
```

**Benefits of the Source Generator Approach:**
- ✅ Works in .NET Native AOT (tested with SQL Server, MySQL, PostgreSQL)
- ✅ Better performance (no reflection overhead)
- ✅ Compile-time type safety
- ✅ Automatic null handling
- ✅ Custom column name mapping with `[JadeDbColumn]`
- ✅ Compatible with standard JIT builds
- ✅ Mix and match approaches as needed

That's it for the setup part


Now using the plugin as as easy as adding the parameter :IDatabaseService dbConfig" to a function inside your controller or the controller constructor. For ease in this example we are goin to add it to the constructor so we have access to it in all the functions of that class.

```
using System.Data;
using System.Diagnostics;
using JadedDbClient.Interfaces;
using Microsoft.AspNetCore.Mvc;
using WebTest.Models;

namespace WebTest.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    IDatabaseService _dbConfig;
    public HomeController(ILogger<HomeController> logger, IDatabaseService dbConfig)
    {
        _dbConfig = dbConfig;
        _logger = logger;
    }
}

```

That's it. We are all ready to start making requests to the databse.


## How to interact with the database

### Understanding Automatic Mapping

**Good News!** 🎉 You don't need to do anything special to use the database methods. The library handles mapping automatically:

- **With `[JadeDbObject]` or manual mappers**: Uses pre-compiled mappers (recommended for AOT)
- **Without pre-compiled mappers**: Falls back to reflection (use standard JIT builds)

**Both approaches work with the same API calls!** Note: For Native AOT, always use `[JadeDbObject]` to avoid reflection.

#### Example: Same Code, Different Mapping Approaches

```csharp
// This code works identically whether you registered a mapper or not!
IEnumerable<UserModel> users = await _dbConfig.ExecuteQueryAsync<UserModel>("SELECT * FROM Users");

// If you registered a mapper for UserModel:
//   -> Uses fast pre-compiled mapper

// If you didn't register a mapper for UserModel:
//   -> Automatically uses reflection (still works!)
```

### All Database Methods

### GetParameter: Created database parameters that you send to databse
Creates a new instance of an <see cref="IDbDataParameter"/> for your Database.
Method Signature: **IDbDataParameter GetParameter(string name, object value, DbType dbType, ParameterDirection direction = ParameterDirection.Input, int size = 0);**

```
//eg: Sample parameterised query

string insrtQry = "INSERT INTO tbl_test(Name) VALUES(@Name);";

List<IDbDataParameter> dbDataParameters = new List<IDbDataParameter>();
dbDataParameters.Add(_dbConfig.GetParameter("@Name", "Someone", DbType.String, ParameterDirection.Input, 250));
```

### ExecuteStoredProcedureWithOutputAsync: Stored Procedure with Output Parameters
Executes a stored procedure asynchronously and retrieves the output parameters.
Method Signature: **Task<Dictionary<string, object>> ExecuteStoredProcedureWithOutputAsync(string storedProcedureName, IEnumerable<IDbDataParameter> parameters);**


```
//Execute a stored proceude with output parameter
List<IDbDataParameter> dbDataParameters = new List<IDbDataParameter>();

dbDataParameters.Add(_dbConfig.GetParameter("p_name", "John Doe", DbType.String, ParameterDirection.Input, 250));
dbDataParameters.Add(_dbConfig.GetParameter("p_OutputParam", "test", DbType.String, ParameterDirection.Output, 250));

Dictionary<string, object> outputParameters = await _dbConfig.ExecuteStoredProcedureWithOutputAsync("add_date", dbDataParameters); 

foreach (var item in outputParameters)
{
    //Print the values of the output parameters. These are parameters that you had set as output
    Console.WriteLine($"{item.Key} : {item.Value}");
}
```


### ExecuteQueryAsync: Execute a query and return results
Executes a SQL query asynchronously and maps the result to a collection of objects of type T.
Method Signature: **Task<IEnumerable<T>> ExecuteQueryAsync<T>(string query, IEnumerable<IDbDataParameter> parameters = null);**

```
//Execute a query
string query = "SELECT * FROM tbl_test;";

IEnumerable<DataModel> results = await _dbConfig.ExecuteQueryAsync<DataModel>(query);
```

### ExecuteQueryFirstRowAsync: Execute a query and return the first row
Executes a SQL query asynchronously and maps the first result row to an object of type T.
Method Signature: **Task<T?> ExecuteQueryFirstRowAsync<T>(string query, IEnumerable<IDbDataParameter> parameters = null);**

```
//Execute a query and get the first row only
string query = "SELECT * FROM tbl_test WHERE Id = @Id;";

List<IDbDataParameter> dbDataParameters = new List<IDbDataParameter>();
dbDataParameters.Add(_dbConfig.GetParameter("@Id", 1, DbType.Int32));

DataModel? result = await _dbConfig.ExecuteQueryFirstRowAsync<DataModel>(query, dbDataParameters);
if (result != null)
{
    // Use the result object
    Console.WriteLine($"Name: {result.Name}");
}
else
{
    Console.WriteLine("No data found.");
}
```

### ExecuteScalar: Executes a query and returns a single data item
Use this function to execute any query which returns a single vaule. eg: row count.
Method Signature: **Task<T?> ExecuteScalar<T>(string query, IEnumerable<IDbDataParameter> parameters = null);**

```
//eg: Bulk Insert data into the table

string checkTableExistsQuery = $"SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'tbl_test');";
bool dataPresent = await _databaseService.ExecuteScalar<bool>(checkTableExistsQuery);

```

### ExecuteStoredProcedureAsync: Execute a stored procedure without output parameters
Executes a stored procedure asynchronously and returns the number of rows affected.
Method Signature: **Task<int> ExecuteStoredProcedureAsync(string storedProcedureName, IEnumerable<IDbDataParameter> parameters = null);**

```
//Execute a stored procedure without output parameter

List<IDbDataParameter> dbDataParameters = new List<IDbDataParameter>();

dbDataParameters.Add(_dbConfig.GetParameter("p_name", "John Doe", DbType.String, ParameterDirection.Input, 250));

int rowsAffected = await _dbConfig.ExecuteStoredProcedureAsync("add_date", dbDataParameters);
```

### ExecuteStoredProcedureSelectDataAsync: Execute a stored procedure that returns data that can be bound to a model
Executes a stored procedure asynchronously and maps the result to a collection of objects of type T.
Method Signature: **Task<IEnumerable<T>> ExecuteStoredProcedureSelectDataAsync<T>(string storedProcedureName, IEnumerable<IDbDataParameter> parameters = null);**

```
//Execute a stored procedure and return data bound to a model class

IEnumerable<DataModel> results = await _dbConfig.ExecuteStoredProcedureSelectDataAsync<DataModel>("get_data", new List<IDbDataParameter> { _dbConfig.GetParameter("p_limit", 100, DbType.Int32, ParameterDirection.Input, 250) });
```

### ExecuteCommandAsync: Execute a DML query to the database
Executes a SQL command asynchronously.
Method Signature: **Task ExecuteCommandAsync(string command, IEnumerable<IDbDataParameter> parameters = null);**

```
//eg: Insert data into the table

string insrtQry = "INSERT INTO tbl_test(Name) VALUES(@Name);";

List<IDbDataParameter> dbDataParameters = new List<IDbDataParameter>();
dbDataParameters.Add(_dbConfig.GetParameter("@Name", "Someone", DbType.String, ParameterDirection.Input, 250));

await _dbConfig.ExecuteCommandAsync(insrtQry, dbDataParameters);
```

### InsertDataTable: Bulk insert a data table into the database
Bulk inserts data into a database table. For this to work, the DataTable columns names need to match the Column names in the actual database and the table also needs to exist.
Method Signature: **Task<bool> InsertDataTable(string tableName, DataTable dataTable);**

```
//eg: Bulk Insert data into the table

DataTable tbl = new DataTable(); // This will be your actual data table with columns mathing your actual database table
string tableName = "tbl_ToInsertInto"; //This will be the name of the table in the database.
await _dbConfig.InsertDataTable(tableName, tbl);

```

### BulkInsertAsync: Stream-based bulk insert with strongly-typed objects
Bulk inserts a collection or stream of strongly-typed objects into a database table with optimized performance. This method is more flexible than InsertDataTable as it works directly with your model classes and doesn't require creating a DataTable.

**Two overloads available:**

#### 1. IEnumerable<T> Overload
Method Signature: **Task<int> BulkInsertAsync<T>(string tableName, IEnumerable<T> items, int batchSize = 1000);**

Best for: In-memory collections, lists, arrays

```csharp
// Example: Bulk insert from a list of objects
public class Product
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public decimal Price { get; set; }
    public int? Stock { get; set; }
}

// Generate or load your data
var products = new List<Product>
{
    new Product { ProductId = 1, ProductName = "Laptop", Price = 999.99m, Stock = 50 },
    new Product { ProductId = 2, ProductName = "Mouse", Price = 25.99m, Stock = 200 },
    new Product { ProductId = 3, ProductName = "Keyboard", Price = 79.99m, Stock = null }
};

// Bulk insert with default batch size (1000)
int rowsInserted = await _dbConfig.BulkInsertAsync("Products", products);
Console.WriteLine($"Inserted {rowsInserted} products");

// Or specify a custom batch size
int rowsInserted = await _dbConfig.BulkInsertAsync("Products", products, batchSize: 500);
```

#### 2. IAsyncEnumerable<T> Overload with Progress Reporting
Method Signature: **Task<int> BulkInsertAsync<T>(string tableName, IAsyncEnumerable<T> items, IProgress<int>? progress = null, int batchSize = 1000);**

Best for: Streaming data from APIs, databases, files, or other async sources

```csharp
// Example: Stream data from an API and bulk insert with progress reporting
public async IAsyncEnumerable<Product> FetchProductsFromApiAsync()
{
    int page = 1;
    while (true)
    {
        var response = await httpClient.GetAsync($"https://api.example.com/products?page={page}");
        var products = await response.Content.ReadFromJsonAsync<List<Product>>();
        
        if (products == null || products.Count == 0)
            break;
            
        foreach (var product in products)
        {
            yield return product;
        }
        
        page++;
    }
}

// Bulk insert with progress reporting
var progress = new Progress<int>(rowCount =>
{
    Console.WriteLine($"Inserted {rowCount} rows so far...");
});

var stream = FetchProductsFromApiAsync();
int totalInserted = await _dbConfig.BulkInsertAsync("Products", stream, progress, batchSize: 1000);
Console.WriteLine($"Completed! Total rows inserted: {totalInserted}");
```

#### Performance Benefits

**Reflection-Free Mode (Recommended):**

When you use `[JadeDbObject]` on your models, bulk insert operations become **reflection-free** and **AOT-compatible**:

```csharp
using JadeDbClient.Attributes;

// Mark your model with [JadeDbObject] for maximum performance
[JadeDbObject]
public partial class Product
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public decimal Price { get; set; }
    public int? Stock { get; set; }
}

// Source generator automatically creates property accessors
// Bulk insert uses reflection-free path automatically
var products = GetProducts();
int inserted = await _dbConfig.BulkInsertAsync("Products", products);
```

**Benefits of Reflection-Free Mode:**
- ✅ **Faster Initialization**: No `typeof(T).GetProperties()` calls
- ✅ **Faster Property Access**: Direct property access via generated delegates
- ✅ **AOT Compatible**: Works with .NET Native AOT
- ✅ **Better Performance**: Eliminates reflection overhead
- ✅ **Zero Configuration**: Just add `[JadeDbObject]` attribute

**Fallback Mode:**

Without `[JadeDbObject]`, bulk insert still works using reflection:

```csharp
// Works automatically, but uses reflection
public class Product  // No [JadeDbObject] attribute
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
}

// Still works, uses reflection fallback
await _dbConfig.BulkInsertAsync("Products", products);
```

**Database-Specific Optimizations:**

**PostgreSQL:**
- Uses native COPY BINARY protocol
- Extremely fast, direct streaming
- Minimal memory overhead

**MySQL:**
- Optimized batched multi-value INSERT statements
- Example: `INSERT INTO table VALUES (row1), (row2), (row3)...`
- Significantly faster than row-by-row inserts
- Reduces network round-trips

**SQL Server:**
- Leverages SqlBulkCopy API
- Batch processing for optimal throughput
- Native high-performance bulk insert

**Key Features:**
- ✅ **Memory Efficient**: Streams data instead of loading everything into memory
- ✅ **Type Safe**: Works directly with your model classes
- ✅ **Progress Tracking**: Optional IProgress<int> for real-time feedback
- ✅ **Configurable Batching**: Adjust batch size for your workload
- ✅ **Async/Await**: Fully asynchronous for non-blocking operations
- ✅ **Nullable Support**: Handles nullable properties correctly
- ✅ **Cross-Database**: Same API works across PostgreSQL, MySQL, and SQL Server

#### When to Use Each Method

| Method | Best For | Use Case | Performance |
|--------|----------|----------|-------------|
| **InsertDataTable** | Legacy code, DataTable sources | When you already have a DataTable | Good |
| **BulkInsertAsync (IEnumerable)** | In-memory collections | Bulk insert from lists, arrays, or collections | Fast (Reflection-free with `[JadeDbObject]`) |
| **BulkInsertAsync (IAsyncEnumerable)** | Streaming sources | API responses, file readers, database streaming | Fast (Reflection-free with `[JadeDbObject]`) |

**Performance Tip:** Always use `[JadeDbObject]` on your models for best performance. The source generator creates reflection-free property accessors at compile time.

## Database Transactions

JadeDbClient now supports database transactions across all three database types (SQL Server, MySQL, and PostgreSQL). Transactions allow you to group multiple database operations into a single atomic unit of work.

### BeginTransaction: Start a database transaction
Begins a new database transaction. The connection will be opened automatically if it's not already open.
Method Signature: **IDbTransaction BeginTransaction();**

```csharp
// Begin a transaction
IDbTransaction transaction = _dbConfig.BeginTransaction();
```

### BeginTransaction (with Isolation Level): Start a transaction with a specific isolation level
Begins a new database transaction with the specified isolation level.
Method Signature: **IDbTransaction BeginTransaction(IsolationLevel isolationLevel);**

```csharp
// Begin a transaction with ReadCommitted isolation level
IDbTransaction transaction = _dbConfig.BeginTransaction(IsolationLevel.ReadCommitted);
```

### CommitTransaction: Commit a transaction
Commits the current database transaction, making all changes permanent.
Method Signature: **void CommitTransaction(IDbTransaction transaction);**

```csharp
// Commit the transaction
_dbConfig.CommitTransaction(transaction);
```

### RollbackTransaction: Rollback a transaction
Rolls back the current database transaction, undoing all changes made within the transaction.
Method Signature: **void RollbackTransaction(IDbTransaction transaction);**

```csharp
// Rollback the transaction
_dbConfig.RollbackTransaction(transaction);
```

### Complete Transaction Example

Here's a complete example showing how to use transactions to ensure data consistency:

```csharp
IDbTransaction transaction = null;
try
{
    // Begin transaction
    transaction = _dbConfig.BeginTransaction();
    
    // Execute multiple operations within the transaction
    string insertQuery1 = "INSERT INTO Orders(CustomerId, OrderDate) VALUES(@CustomerId, @OrderDate);";
    List<IDbDataParameter> params1 = new List<IDbDataParameter>();
    params1.Add(_dbConfig.GetParameter("@CustomerId", 1, DbType.Int32));
    params1.Add(_dbConfig.GetParameter("@OrderDate", DateTime.Now, DbType.DateTime));
    
    await _dbConfig.ExecuteCommandAsync(insertQuery1, params1);
    
    string insertQuery2 = "INSERT INTO OrderItems(OrderId, ProductId, Quantity) VALUES(@OrderId, @ProductId, @Quantity);";
    List<IDbDataParameter> params2 = new List<IDbDataParameter>();
    params2.Add(_dbConfig.GetParameter("@OrderId", 1, DbType.Int32));
    params2.Add(_dbConfig.GetParameter("@ProductId", 100, DbType.Int32));
    params2.Add(_dbConfig.GetParameter("@Quantity", 5, DbType.Int32));
    
    await _dbConfig.ExecuteCommandAsync(insertQuery2, params2);
    
    // If all operations succeed, commit the transaction
    _dbConfig.CommitTransaction(transaction);
    Console.WriteLine("Transaction committed successfully.");
}
catch (Exception ex)
{
    // If any operation fails, rollback the transaction
    if (transaction != null)
    {
        _dbConfig.RollbackTransaction(transaction);
        Console.WriteLine("Transaction rolled back due to error: " + ex.Message);
    }
}
finally
{
    // Clean up
    transaction?.Dispose();
    _dbConfig.CloseConnection();
}
```

### Transaction Isolation Levels

JadeDbClient supports all standard isolation levels:
- `IsolationLevel.ReadUncommitted`
- `IsolationLevel.ReadCommitted` (default for most databases)
- `IsolationLevel.RepeatableRead`
- `IsolationLevel.Serializable`
- `IsolationLevel.Snapshot` (SQL Server only)

Choose the appropriate isolation level based on your concurrency requirements and database system.

## Complete Real-World Example

Here's a complete example showing how to use JadeDbClient with the Source Generator approach:

### Scenario: E-commerce Order Management

#### Step 1: Define Your Models with `[JadeDbObject]`

```csharp
using JadeDbClient.Attributes;

// Simply add [JadeDbObject] attribute - mappers are generated automatically!
[JadeDbObject]
public partial class Order
{
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}

[JadeDbObject]
public partial class Customer
{
    public int CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

[JadeDbObject]
public partial class Product
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? Stock { get; set; }  // Nullable - handled automatically
}
```

#### Step 2: Setup (Program.cs)

```csharp
using JadeDbClient.Initialize;

var builder = WebApplication.CreateBuilder(args);

// That's it! The Source Generator automatically registers all [JadeDbObject] models
// No manual mapper registration needed!
builder.Services.AddJadeDbService();

var app = builder.Build();
app.Run();
```

#### Step 3: Using in Your Service/Controller

```csharp
using System.Data;
using JadeDbClient.Interfaces;

public class OrderService
{
    private readonly IDatabaseService _dbConfig;
    
    public OrderService(IDatabaseService dbConfig)
    {
        _dbConfig = dbConfig;
    }
    
    // All models with [JadeDbObject] use optimized generated mappers
    public async Task<IEnumerable<Order>> GetAllOrdersAsync()
    {
        string query = "SELECT * FROM Orders WHERE Status = @Status";
        
        var parameters = new List<IDbDataParameter>
        {
            _dbConfig.GetParameter("@Status", "Active", DbType.String)
        };
        
        // If Order mapper is registered -> uses pre-compiled mapper
        // If not registered -> uses automatic reflection
        return await _dbConfig.ExecuteQueryAsync<Order>(query, parameters);
    }
    
    // Get customer details (works with or without registered mapper!)
    public async Task<Customer?> GetCustomerAsync(int customerId)
    {
        string query = "SELECT * FROM Customers WHERE CustomerId = @Id";
        
        var parameters = new List<IDbDataParameter>
        {
            _dbConfig.GetParameter("@Id", customerId, DbType.Int32)
        };
        
        return await _dbConfig.ExecuteQueryFirstRowAsync<Customer>(query, parameters);
    }
    
    // Get products (automatically uses reflection since no mapper registered)
    public async Task<IEnumerable<Product>> GetProductsAsync()
    {
        string query = "SELECT * FROM Products";
        
        // Product doesn't have a registered mapper, so it uses reflection
        // This is perfectly fine for less frequently-queried models!
        return await _dbConfig.ExecuteQueryAsync<Product>(query);
    }
    
    // Create order with transaction
    public async Task<bool> CreateOrderAsync(Order order, List<OrderItem> items)
    {
        IDbTransaction? transaction = null;
        try
        {
            transaction = _dbConfig.BeginTransaction();
            
            // Insert order
            string insertOrderQuery = @"
                INSERT INTO Orders (CustomerId, OrderDate, TotalAmount, Status) 
                VALUES (@CustomerId, @OrderDate, @TotalAmount, @Status);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";
            
            var orderParams = new List<IDbDataParameter>
            {
                _dbConfig.GetParameter("@CustomerId", order.CustomerId, DbType.Int32),
                _dbConfig.GetParameter("@OrderDate", order.OrderDate, DbType.DateTime),
                _dbConfig.GetParameter("@TotalAmount", order.TotalAmount, DbType.Decimal),
                _dbConfig.GetParameter("@Status", order.Status, DbType.String)
            };
            
            int orderId = await _dbConfig.ExecuteScalar<int>(insertOrderQuery, orderParams);
            
            // Insert order items
            foreach (var item in items)
            {
                string insertItemQuery = @"
                    INSERT INTO OrderItems (OrderId, ProductId, Quantity, Price) 
                    VALUES (@OrderId, @ProductId, @Quantity, @Price)";
                
                var itemParams = new List<IDbDataParameter>
                {
                    _dbConfig.GetParameter("@OrderId", orderId, DbType.Int32),
                    _dbConfig.GetParameter("@ProductId", item.ProductId, DbType.Int32),
                    _dbConfig.GetParameter("@Quantity", item.Quantity, DbType.Int32),
                    _dbConfig.GetParameter("@Price", item.Price, DbType.Decimal)
                };
                
                await _dbConfig.ExecuteCommandAsync(insertItemQuery, itemParams);
            }
            
            _dbConfig.CommitTransaction(transaction);
            return true;
        }
        catch (Exception)
        {
            transaction?.Rollback();
            throw;
        }
        finally
        {
            transaction?.Dispose();
            _dbConfig.CloseConnection();
        }
    }
}
```

### Key Takeaways

1. **Zero Boilerplate**: Just add `[JadeDbObject]` to your models - from 35 lines to 1 attribute!
2. **Automatic Registration**: Source Generator uses `[ModuleInitializer]` for instant availability
3. **Mix and match**: Use `[JadeDbObject]` for your models, manual registration for third-party models, or standard JIT builds for dynamic scenarios
4. **AOT Support**: Works in our testing with .NET Native AOT (SQL Server, MySQL, PostgreSQL) - **thorough testing mandatory**
5. **Null Safety**: Nullable types (`int?`, `DateTime?`, `string?`) handled automatically


## Native AOT Compatibility & Limitations
 
**JadeDbClient** is designed to be AOT-friendly by using Source Generators to avoid runtime reflection for object mapping. 

**Testing Results**: In our testing, JadeDbClient worked successfully with .NET Native AOT for SQL Server, MySQL, and PostgreSQL, though with expected trimming warnings from database drivers.
 
**Important - Use with Caution**:

1. **Database Driver Warnings**: The underlying database drivers (`Microsoft.Data.SqlClient`, `MySqlConnector`, `Npgsql`) produce trim/AOT warnings during Native AOT publish (e.g., `IL2104`, `IL3053`). These warnings are:
   - **Expected and documented** by the driver maintainers
   - **Outside of JadeDbClient's control** - they originate from the driver packages
   - **Not blocking compilation** - your application will compile and run

2. **Testing is Non-Negotiable**: Due to the aggressive trimming nature of .NET Native AOT, thorough testing of every functionality is **essential** before production deployment. Without `[JadeDbObject]`, the application may fall back to reflection mode, which can cause unexpected behaviors in AOT builds.

3. **Normal JIT Builds**: If you do *not* use `[JadeDbObject]` and need reflection support, use standard .NET JIT builds instead of Native AOT.

**Example AOT Warnings You'll See**:
```bash
warning IL2104: Assembly 'Microsoft.Data.SqlClient' produced trim warnings
warning IL3053: Assembly 'Microsoft.Data.SqlClient' produced AOT analysis warnings
warning IL2104: Assembly 'MySqlConnector' produced trim warnings
warning IL2104: Assembly 'System.Configuration.ConfigurationManager' produced trim warnings
```

**Recommendation**: 
- ✅ Always use `[JadeDbObject]` for your models in AOT applications
- ⚠️ **Testing is mandatory** - test every functionality thoroughly in a staging environment
- ⚠️ Use Native AOT with caution - aggressive trimming may cause unexpected behaviors
- ✅ Monitor database driver releases for AOT compatibility improvements
- ✅ For dynamic scenarios, use standard JIT builds instead

## ⚠️ Query Builder *(Beta)*

> **This feature is in beta.** Always review and test generated SQL queries in a staging environment before deploying to production.

`QueryBuilder<T>` provides a fluent, type-safe API for building parameterised SELECT, INSERT, UPDATE, and DELETE statements without writing raw SQL — including list-based `IN` filters via `.In()`. Because queries are generated dynamically at runtime, it is **essential** to validate the generated output before relying on it in production.

### Model Setup

Decorate your model with `[JadeDbTable]` to specify the table name (optional — the builder pluralises the class name by default), `[JadeDbColumn]` for any column-name differences, and `[JadeDbColumn(IgnoreOnInsert = true)]` for any column whose value is generated by the database (IDENTITY / AUTO_INCREMENT / computed defaults):

```csharp
using JadeDbClient.Attributes;

[JadeDbTable("products")]
public class Product
{
    // Database generates this value — excluded from INSERT and UPDATE automatically.
    // IsIdentity = true tells BuildInsert(returnIdentity: true) which column to return.
    [JadeDbColumn(IgnoreOnInsert = true, IsIdentity = true)]
    public int Id { get; set; }

    [JadeDbColumn("product_name")]
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    [JadeDbColumn("category_id")]
    public int CategoryId { get; set; }
}
```

> **No conventions are assumed.** Unless a property is explicitly decorated with `[JadeDbColumn(IgnoreOnInsert = true)]`, it will always be included in INSERT and UPDATE statements. This keeps every schema flexible — models without auto-generated keys work without any special configuration.

> **`IsIdentity`**: When calling `BuildInsert(returnIdentity: true)`, the builder looks for the first property decorated with `[JadeDbColumn(IsIdentity = true)]` to determine which column name appears in the database-specific identity clause (`RETURNING <col>` for PostgreSQL, `OUTPUT INSERTED.<col>` for SQL Server, `SELECT LAST_INSERT_ID()` for MySQL). If no property carries `IsIdentity = true`, the fallback is the column name `id`.

### SELECT

```csharp
var qb = new QueryBuilder<Product>(_dbService);

// Simple SELECT – all columns
var (sql, parameters) = qb.BuildSelect();
// → SELECT Id, product_name, Price, category_id FROM products

// Filter, order, page
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Where(p => p.Price > 10.0m && p.Name.Contains("Widget"))
    .OrderBy(p => p.Name)
    .ThenByDescending(p => p.Price)
    .Skip(20)
    .Take(10)
    .BuildSelect();

// Type-safe column selection – single property
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Select(p => p.Name)
    .BuildSelect();
// → SELECT product_name FROM products

// Type-safe column selection – anonymous type projection
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Select(p => new { p.Name, p.Price })
    .BuildSelect();
// → SELECT product_name, Price FROM products

// Raw string columns (validated; caller is responsible for table-qualification with joins)
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Select("Id", "product_name")
    .BuildSelect();
```

### Executing queries

Instead of calling `BuildSelect()` and passing the result to `_dbService.ExecuteQueryAsync` manually, you can use the built-in terminal execution methods to do both in one step:

```csharp
// Example DTO used in the typed-result snippets below
public class ProductSummaryDto
{
    public string product_name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
```

```csharp
// ── Typed results ─────────────────────────────────────────────────────────

// Returns IEnumerable<Product> (maps to the main model type T)
IEnumerable<Product> products = await new QueryBuilder<Product>(_dbService)
    .Where(p => p.Price > 10m)
    .OrderBy(p => p.Name)
    .ToListAsync();

// Returns IEnumerable<TResult> — useful when your SELECT columns map to a DTO
// rather than the main model type (e.g. after a .Select(p => new { p.Name, p.Price }))
IEnumerable<ProductSummaryDto> summaries = await new QueryBuilder<Product>(_dbService)
    .Where(p => p.CategoryId == 3)
    .ToListAsync<ProductSummaryDto>();

// First matching row mapped to T, or null if no rows
Product? product = await new QueryBuilder<Product>(_dbService)
    .Where(p => p.Id == 42)
    .FirstOrDefaultAsync();

// First matching row as a DTO — useful after a projected SELECT
ProductSummaryDto? summary = await new QueryBuilder<Product>(_dbService)
    .Where(p => p.Id == 42)
    .FirstOrDefaultAsync<ProductSummaryDto>();

// ── Dynamic results (for JOIN queries spanning multiple tables) ────────────

// Returns IEnumerable<dynamic> — each row is an ExpandoObject whose properties
// match the column names returned by the query.
IEnumerable<dynamic> rows = await new QueryBuilder<Product>(_dbService)
    .Join<Category>((p, c) => p.CategoryId == c.Id)
    .LeftJoin<Order>((p, o) => p.Id == o.Id)
    .SelectColumns(cols => cols
        .From<Product>(p => new { p.Name, p.Price })
        .From<Category>(c => c.Name)
        .From<Order>(o => o.Total))
    .Where(p => p.Price > 10m)
    .ToDynamicListAsync();

foreach (dynamic row in rows)
{
    // Access columns by name through the dictionary interface (AOT-safe)
    var dict = (IDictionary<string, object?>)row;
    Console.WriteLine($"{dict["product_name"]} – {dict["category_name"]}");
}

// First dynamic row from a JOIN query, or null if no rows
dynamic? firstRow = await new QueryBuilder<Product>(_dbService)
    .Join<Category>((p, c) => p.CategoryId == c.Id)
    .SelectColumns(cols => cols
        .From<Product>(p => p.Name)
        .From<Category>(c => c.Name))
    .Where(p => p.Id == 1)
    .FirstOrDefaultDynamicAsync();

if (firstRow != null)
{
    var dict = (IDictionary<string, object?>)firstRow;
    Console.WriteLine(dict["product_name"]);
}
```

> **AOT note:** `ExpandoObject` is fully Native AOT-safe. Access its values through
> `(IDictionary<string, object?>)row` rather than `row.PropertyName` syntax to avoid
> any DLR dispatch — this also gives you direct dictionary access without dynamic
> binding overhead.

### Counting rows

`CountAsync()` executes a `SELECT COUNT(*)` query using all configured WHERE filters and JOINs. The SELECT column list, ORDER BY, and paging settings are ignored — only FROM, JOIN, and WHERE are used.

```csharp
// Total rows in the table
long total = await new QueryBuilder<Product>(_dbService)
    .CountAsync();

// Rows matching a filter
long active = await new QueryBuilder<Product>(_dbService)
    .Where(p => p.Price > 0m)
    .CountAsync();

// Rows matching a filter across a JOIN
long matched = await new QueryBuilder<Product>(_dbService)
    .Join<Category>((p, c) => p.CategoryId == c.Id)
    .Where(p => p.Price > 10m)
    .CountAsync();
```

### JOINs

Use `Join<TJoin>`, `LeftJoin<TJoin>`, `RightJoin<TJoin>`, or `FullJoin<TJoin>` to combine tables. The ON condition is expressed as a two-parameter lambda — both property names and column attributes are resolved automatically.

When any join is added, all unqualified column names in SELECT, WHERE, and ORDER BY are automatically prefixed with the main table name to prevent ambiguity.

```csharp
[JadeDbTable("categories")]
public class Category
{
    public int Id { get; set; }

    [JadeDbColumn("category_name")]
    public string Name { get; set; } = string.Empty;
}

[JadeDbTable("orders")]
public class Order
{
    public int Id { get; set; }

    [JadeDbColumn("customer_id")]
    public int CustomerId { get; set; }

    public decimal Total { get; set; }
}

// INNER JOIN (default) – SELECT only columns from the main table
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Join<Category>((product, category) => product.CategoryId == category.Id)
    .Select(p => new { p.Name, p.Price })
    .Where(p => p.Price > 10m)
    .OrderBy(p => p.Name)
    .BuildSelect();
// → SELECT products.product_name, products.Price
//   FROM products
//   INNER JOIN categories ON (products.category_id = categories.Id)
//   WHERE (products.Price > @p0)
//   ORDER BY products.product_name ASC

// LEFT JOIN
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .LeftJoin<Category>((p, c) => p.CategoryId == c.Id)
    .BuildSelect();

// Compound ON condition
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Join<Category>((p, c) => p.CategoryId == c.Id && p.Price > 0m)
    .BuildSelect();
```

#### Selecting columns from joined tables

There are three ways to include columns from joined tables in the SELECT list, depending on how many joins you have.

**Option 1 — type-safe two-parameter expression (one join)**

Use `Select<TJoin, TResult>` with a two-parameter lambda when you have a single join. Each column reference is automatically qualified with its correct table name, and `[JadeDbColumn]` attributes are respected.

```csharp
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Join<Category>((p, c) => p.CategoryId == c.Id)
    .Select((Product p, Category c) => new { ProductName = p.Name, CategoryName = c.Name })
    .Where(p => p.Price > 10m)
    .BuildSelect();
// → SELECT products.product_name, categories.category_name
//   FROM products
//   INNER JOIN categories ON (products.category_id = categories.Id)
//   WHERE (products.Price > @p0)

// Single column from the joined table
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Join<Category>((p, c) => p.CategoryId == c.Id)
    .Select((Product p, Category c) => c.Name)
    .BuildSelect();
// → SELECT categories.category_name FROM products INNER JOIN categories ON …
```

> **Note:** Because C# anonymous types cannot have two properties with the same name, use named properties when both tables share a property name (e.g., `new { ProductName = p.Name, CategoryName = c.Name }`).

**Option 2 — `SelectColumns` fluent builder (one or more joins)**

Use `SelectColumns(cols => cols.From<TTable>(…)…)` when you have multiple joins or want a more explicit, readable selection across many tables. Chain as many `.From<TTable>()` calls as needed — each one appends one or more columns from that table.

```csharp
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Join<Category>((p, c) => p.CategoryId == c.Id)
    .LeftJoin<Order>((p, o) => p.Id == o.Id)
    .SelectColumns(cols => cols
        .From<Product>(p => new { p.Name, p.Price })   // two columns from products
        .From<Category>(c => c.Name)                   // one column from categories
        .From<Order>(o => o.Total))                    // one column from orders
    .Where(p => p.Price > 10m)
    .BuildSelect();
// → SELECT products.product_name, products.Price, categories.category_name, orders.Total
//   FROM products
//   INNER JOIN categories ON (products.category_id = categories.Id)
//   LEFT JOIN orders ON (products.Id = orders.Id)
//   WHERE (products.Price > @p0)
```

Each `.From<TTable>()` call supports:
- A single property: `.From<Category>(c => c.Name)`
- An anonymous-type projection: `.From<Product>(p => new { p.Name, p.Price })`

Column names are always resolved via `[JadeDbColumn]` when present.

**Option 3 — raw-string `Select` (full manual control)**

Pass pre-qualified column strings directly. All validation rules for safe SQL identifiers still apply; no automatic qualification is performed.

```csharp
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Join<Category>((p, c) => p.CategoryId == c.Id)
    .Select("products.product_name", "categories.category_name")
    .BuildSelect();
// → SELECT products.product_name, categories.category_name FROM products INNER JOIN …
```

### INSERT

Properties marked with `[JadeDbColumn(IgnoreOnInsert = true)]` are automatically excluded from both INSERT and UPDATE statements — you never need to filter them manually.

```csharp
var product = new Product { Name = "Gadget", Price = 29.99m, CategoryId = 3 };

// Plain INSERT
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .BuildInsert(product);

// INSERT and return the new identity / serial value
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .BuildInsert(product, returnIdentity: true);
```

### UPDATE

```csharp
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Where(p => p.Id == 42)
    .BuildUpdate(updatedProduct);
```

### DELETE

```csharp
var (sql, parameters) = new QueryBuilder<Product>(_dbService)
    .Where(p => p.CategoryId == 5)
    .BuildDelete();
```

### Filtering with `.In()` (list / collection values)

Filter a column against a collection of values using the `In` extension method from `JadeDbClient.Helpers`. Every value is emitted as its own parameter — nothing is inlined into the SQL string.

Supported inputs include `List<T>`, arrays (`int[]`, `string[]`, etc.), and any `IEnumerable<T>` held in a variable.

```csharp
using JadeDbClient.Helpers;

// List<int>
var categoryIds = new List<int> { 1, 5, 10 };
var products = await new QueryBuilder<Product>(_dbService)
    .Where(p => p.CategoryId.In(categoryIds))
    .ToListAsync();
// → SELECT … FROM products WHERE category_id IN (@p0, @p1, @p2)

// string array
var statuses = new[] { "active", "shipped", "pending" };
var (sql, parameters) = new QueryBuilder<Order>(_dbService)
    .Where(o => o.Status.In(statuses))
    .BuildSelect();
// → SELECT … FROM orders WHERE Status IN (@p0, @p1, @p2)

// Combined with other WHERE conditions
var results = await new QueryBuilder<Product>(_dbService)
    .Where(p => p.CategoryId.In(categoryIds) && p.Price > 10m)
    .ToListAsync();
// → WHERE (category_id IN (@p0, @p1, @p2) AND Price > @p3)
```

**Empty collections:** When the list has no items, the builder generates `1=0` (always-false) instead of invalid `IN ()` syntax. The query returns zero rows safely — useful when the allowed IDs come from user input that may be empty.

```csharp
var emptyIds = new List<int>();
var none = await new QueryBuilder<Product>(_dbService)
    .Where(p => p.CategoryId.In(emptyIds))
    .ToListAsync();
// → SELECT … FROM products WHERE 1=0
```

`.In()` works anywhere `.Where()` is accepted — `BuildSelect`, `ToListAsync`, `CountAsync`, `BuildUpdate`, and `BuildDelete`.

### Supported WHERE operators

| Expression | Generated SQL |
|---|---|
| `p.Price > 10` | `(Price > @p0)` |
| `p.Name == "X"` | `(product_name = @p0)` |
| `p.Name.ToLower() == "x"` | `(LOWER(product_name) = @p0)` |
| `p.Name.ToUpper() == "X"` | `(UPPER(product_name) = @p0)` |
| `p.Name.Trim() == "X"` | `(TRIM(product_name) = @p0)` (also `TrimStart()` → `LTRIM`, `TrimEnd()` → `RTRIM`) |
| `p.Name.Length > 5` | `(LENGTH(product_name) > @p0)` (or `LEN(…)` on SQL Server) |
| `p.Name.Contains("X")` | `product_name LIKE @p0 ESCAPE '~'` (or `ILIKE` on PostgreSQL) |
| `p.Name.StartsWith("X")` | `product_name LIKE @p0 ESCAPE '~'` |
| `p.Name.EndsWith("X")` | `product_name LIKE @p0 ESCAPE '~'` |
| `p.CategoryId.In(ids)` | `category_id IN (@p0, @p1, …)` — empty list → `1=0` |
| `p.Name == myVar.ToLower()` | Evaluates captured C# expressions to parameter value `@p0` |
| `&&` / `\|\|` | `AND` / `OR` |
| `!` | `NOT (…)` |

### JOIN reference

| Method | SQL keyword |
|---|---|
| `.Join<TJoin>(on)` | `INNER JOIN` |
| `.LeftJoin<TJoin>(on)` | `LEFT JOIN` |
| `.RightJoin<TJoin>(on)` | `RIGHT JOIN` |
| `.FullJoin<TJoin>(on)` | `FULL JOIN` |

> **Column qualification with joins**: When one or more joins are added, any unqualified column name in SELECT, WHERE, and ORDER BY that was derived from an expression (or from the default *all-columns* selection) is automatically prefixed with the main table name to prevent ambiguity. If you use the raw-string `Select("col1", "col2")` overload, you are responsible for qualifying any column names that could be ambiguous.

### Execution method reference

| Method | Returns | Notes |
|---|---|---|
| `.ToListAsync()` | `Task<IEnumerable<T>>` | Builds and executes SELECT, maps rows to main type `T` |
| `.ToListAsync<TResult>()` | `Task<IEnumerable<TResult>>` | Builds and executes SELECT, maps rows to `TResult` |
| `.ToDynamicListAsync()` | `Task<IEnumerable<dynamic>>` | Builds and executes SELECT, returns `ExpandoObject` rows — use for multi-table JOINs |
| `.FirstOrDefaultAsync()` | `Task<T?>` | First row mapped to `T`, or `null` |
| `.FirstOrDefaultAsync<TResult>()` | `Task<TResult?>` | First row mapped to `TResult`, or `null` |
| `.FirstOrDefaultDynamicAsync()` | `Task<dynamic?>` | First row as `ExpandoObject`, or `null` — use for multi-table JOINs |
| `.CountAsync()` | `Task<long>` | Executes `SELECT COUNT(*)`, respecting WHERE and JOIN clauses |

> **AOT:** `ToListAsync<TResult>` / `FirstOrDefaultAsync<TResult>` carry `[DynamicallyAccessedMembers]` on `TResult` — the same guarantee as `ExecuteQueryAsync<T>`. The `dynamic` overloads (`ToDynamicListAsync`, `FirstOrDefaultDynamicAsync`) use `ExpandoObject` with no reflection and are fully Native AOT-safe. `CountAsync()` uses `ExecuteScalar<long>` and is also fully AOT-safe.

### Security notes

- **All values are parameterised** — user-supplied values never appear inline in the SQL string.
- **LIKE wildcards are automatically escaped** — characters such as `%`, `_`, `~`, and (on SQL Server) `[` in string values are escaped before being passed as parameters, preventing unintended wildcard matches.
- **Column names passed to `Select(string[])` and the legacy `OrderBy(string)` are validated** — only safe SQL identifiers (alphanumeric, underscores, dots, and standard quoting styles) are accepted; any other input throws an `ArgumentException`. Prefer expression overloads.
- **Empty `In()` lists** — passing an empty collection to `.In(values)` generates a safe always-false predicate (`1=0`) instead of invalid SQL syntax (`IN ()`).
- **Prefer expression-based overloads** — `Select(p => new { p.Name })`, `OrderBy(p => p.CreatedAt)`, and `Join<TJoin>((p, j) => …)` all resolve column names through the type system and respect `[JadeDbColumn]` attributes.

### Beta limitations & recommendations

> ⚠️ **Review every generated query before production use.**

1. **Always inspect the generated SQL** — log or print `sql` in development to confirm the query is correct for your schema.
2. **Test all code paths** — run your application against a staging database and verify INSERT, UPDATE, DELETE, JOIN, and complex WHERE clauses produce the expected rows and row counts.
3. **Pagination on SQL Server requires ORDER BY** — calling `Skip`/`Take` without at least one `OrderBy` throws `InvalidOperationException`.
4. **UPDATE and DELETE require a WHERE clause** — omitting `.Where(…)` before `BuildUpdate` / `BuildDelete` throws `InvalidOperationException` to prevent accidental full-table modifications.
5. **Column exclusion from writes** — `BuildInsert` and `BuildUpdate` only exclude a column when it is explicitly decorated with `[JadeDbColumn(IgnoreOnInsert = true)]`. There are no name-based conventions, so every schema — including those without a database-generated primary key — works without extra configuration.
6. **JOIN result mapping** — Use `ToDynamicListAsync()` / `FirstOrDefaultDynamicAsync()` to receive JOIN results as `ExpandoObject` rows when no single model type represents the full result set. Access columns through `(IDictionary<string, object?>)row` for full AOT compatibility.
7. **Complex expressions** — member access, binary comparisons, string functions (`Contains`, `StartsWith`, `EndsWith`, `ToLower`, `ToUpper`, `Trim`, `TrimStart`, `TrimEnd`, `Length`), evaluated C# expressions/variables, and the `In` extension are translated. Unsupported expressions throw `NotSupportedException`.

---

## 📚 Documentation
- **[GitHub Repository](https://github.com/KaustavCodes/JadeDbClient-DotNet)** - Source code and issue tracker
- **[Changelog](https://github.com/KaustavCodes/JadeDbClient-DotNet/blob/main/CHANGELOG.md)** - Version history and release notes

Happy Coding! 

## License

This project is licensed under the MIT License.
