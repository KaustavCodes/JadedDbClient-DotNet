using JadeDbClient;
using JadeDbClient.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace JadeDbClient.Initialize;

public static class JadeDbServiceRegistration
{
    public class JadeDbServiceOptions
    {
        public bool EnableLogging { get; set; } = false;

        public bool LogExecutedQuery { get; set; } = false;

        public bool PluralizeTableName { get; set; } = false;

        /// <summary>
        /// Gets or sets the DI service lifetime for IDatabaseService registrations. Defaults to <see cref="ServiceLifetime.Singleton"/>.
        /// </summary>
        public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Singleton;
    }

    public static void AddJadeDbService(this IServiceCollection services, Action<JadeDbMapperOptions>? configure = null, Action<JadeDbServiceOptions>? serviceOptionsConfigure = null)
    {
        var options = new JadeDbMapperOptions();
        configure?.Invoke(options);

        var serviceOptions = new JadeDbServiceOptions();
        serviceOptionsConfigure?.Invoke(serviceOptions);

        // Register JadeDbMapperOptions as a singleton
        services.AddSingleton(options);
        // Register JadeDbServiceOptions as a singleton
        services.AddSingleton(serviceOptions);

        // Setup the database configuration service
        services.AddSingleton<DatabaseConfigurationService>();

        // Factory to create IDatabaseService
        IDatabaseService Factory(IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var databaseConfigService = serviceProvider.GetRequiredService<DatabaseConfigurationService>();
            var mapperOptions = serviceProvider.GetRequiredService<JadeDbMapperOptions>();
            var dbServiceOptions = serviceProvider.GetRequiredService<JadeDbServiceOptions>();
            var databaseType = databaseConfigService.GetDatabaseType();

            if (dbServiceOptions.EnableLogging)
            {
                Console.WriteLine($"[JadeDbClient] DatabaseType: {databaseType}");
            }

            return databaseType switch
            {
                "MsSql" => (IDatabaseService)new MsSqlDbService(configuration, mapperOptions, dbServiceOptions),
                "MySql" => (IDatabaseService)new MySqlDbService(configuration, mapperOptions, dbServiceOptions),
                "PostgreSQL" => (IDatabaseService)new PostgreSqlDbService(configuration, mapperOptions, dbServiceOptions),
                _ => throw new Exception("Unsupported database type"),
            };
        }

        // Register the database service using the configured lifetime
        services.Add(new ServiceDescriptor(typeof(IDatabaseService), Factory, serviceOptions.Lifetime));
    }

    /// <summary>
    /// Registers multiple named database connections and an <see cref="IJadeDbServiceFactory"/> that can resolve them by name.
    /// <para>
    /// The recommended approach is to define all connections in <c>appsettings.json</c> under
    /// <c>JadeDb:Connections</c>. The key you give each entry (e.g. <c>"main"</c>, <c>"reports"</c>) is
    /// exactly the name used to resolve the connection — no additional mapping is required.
    /// Set <c>JadeDb:DefaultConnection</c> to designate which connection is injected as
    /// <see cref="Interfaces.IDatabaseService"/> directly.
    /// </para>
    /// <para>
    /// Optionally pass a <paramref name="configure"/> action to add or override connections at startup
    /// (e.g. for dynamically computed connection strings). Programmatic entries always take precedence
    /// over configuration values with the same name.
    /// </para>
    /// <para>
    /// When a default connection is designated (via the <c>JadeDb:DefaultConnection</c> config key or
    /// <see cref="JadeDbNamedConnectionsBuilder.SetDefaultConnection"/>),
    /// <see cref="Interfaces.IDatabaseService"/> is also registered in the DI container so that existing
    /// code injecting <see cref="Interfaces.IDatabaseService"/> directly continues to work unchanged.
    /// </para>
    /// <example>
    /// appsettings.json — the connection keys ("main", "reports") become the names used in code:
    /// <code>
    /// {
    ///   "JadeDb": {
    ///     "DefaultConnection": "main",
    ///     "Connections": {
    ///       "main":    { "DatabaseType": "MsSql",     "ConnectionString": "..." },
    ///       "reports": { "DatabaseType": "PostgreSQL", "ConnectionString": "..." }
    ///     }
    ///   }
    /// }
    /// </code>
    /// Program.cs — zero connection strings in code:
    /// <code>
    /// builder.Services.AddJadeDbNamedConnections(
    ///     mapperConfigure: options =>
    ///     {
    ///         // Only needed for third-party models you cannot decorate with [JadeDbObject]
    ///         options.RegisterMapper&lt;ThirdPartyModel&gt;(reader => new ThirdPartyModel
    ///         {
    ///             Id   = reader.GetInt32(reader.GetOrdinal("Id")),
    ///             Name = reader.GetString(reader.GetOrdinal("Name"))
    ///         });
    ///     },
    ///     serviceOptionsConfigure: options =>
    ///     {
    ///         options.EnableLogging    = true;   // log query timing (default: false)
    ///         options.LogExecutedQuery = true;   // log executed SQL (default: false)
    ///     });
    /// </code>
    /// Inject the default connection directly — the "main" key from appsettings:
    /// <code>
    /// public class OrderService(IDatabaseService db)  // receives the default ("main")
    /// {
    ///     public Task&lt;IEnumerable&lt;Order&gt;&gt; GetOrdersAsync()
    ///         => db.ExecuteQueryAsync&lt;Order&gt;("SELECT * FROM Orders");
    /// }
    /// </code>
    /// Resolve any connection by its appsettings key via the factory:
    /// <code>
    /// public class ReportService(IJadeDbServiceFactory dbFactory)
    /// {
    ///     public async Task DoWork()
    ///     {
    ///         var main    = dbFactory.GetService();          // returns the default ("main")
    ///         var reports = dbFactory.GetService("reports"); // the "reports" key from appsettings
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </summary>
    public static void AddJadeDbNamedConnections(
        this IServiceCollection services,
        Action<JadeDbNamedConnectionsBuilder>? configure = null,
        Action<JadeDbMapperOptions>? mapperConfigure = null,
        Action<JadeDbServiceOptions>? serviceOptionsConfigure = null)
    {
        var mapperOptions = new JadeDbMapperOptions();
        mapperConfigure?.Invoke(mapperOptions);

        var serviceOptions = new JadeDbServiceOptions();
        serviceOptionsConfigure?.Invoke(serviceOptions);

        services.AddSingleton<IJadeDbServiceFactory>(serviceProvider =>
        {
            var configuration = serviceProvider.GetService<IConfiguration>();

            var builder = new JadeDbNamedConnectionsBuilder();

            // Load connections from the JadeDb:Connections configuration section first.
            if (configuration != null)
            {
                var connectionsSection = configuration.GetSection("JadeDb:Connections");
                foreach (var child in connectionsSection.GetChildren())
                {
                    var dbType = child["DatabaseType"] ?? string.Empty;
                    var connStr = child["ConnectionString"] ?? string.Empty;
                    if (!string.IsNullOrEmpty(dbType) && !string.IsNullOrEmpty(connStr))
                        builder.AddConnection(child.Key, dbType, connStr);
                }

                // Read the default connection name from config if not already set programmatically.
                var configDefault = configuration["JadeDb:DefaultConnection"];
                if (!string.IsNullOrWhiteSpace(configDefault))
                    builder.SetDefaultConnection(configDefault);
            }

            // Apply programmatic configuration (overrides config-based entries with the same name).
            // Programmatic SetDefaultConnection also overrides the config-based one.
            configure?.Invoke(builder);

            var namedServices = new Dictionary<string, IDatabaseService>();
            foreach (var (name, (dbType, connStr)) in builder.Connections)
            {
                namedServices[name] = CreateDbService(dbType, connStr, mapperOptions, serviceOptions);
            }

            // Resolve the default service if one has been designated.
            IDatabaseService? defaultService = null;
            if (builder.DefaultConnectionName != null)
            {
                if (!namedServices.TryGetValue(builder.DefaultConnectionName, out defaultService))
                    throw new InvalidOperationException(
                        $"The default connection '{builder.DefaultConnectionName}' was not found. " +
                        $"Registered connections: {string.Join(", ", namedServices.Keys)}");
            }

            return new JadeDbServiceFactory(namedServices, defaultService);
        });

        // Also register IDatabaseService so it can be injected directly without the factory.
        // It delegates to the factory's default service.
        services.AddSingleton<IDatabaseService>(serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<IJadeDbServiceFactory>();
            return factory.GetService();
        });
    }

    internal static IDatabaseService CreateDbService(
        string databaseType,
        string connectionString,
        JadeDbMapperOptions mapperOptions,
        JadeDbServiceOptions serviceOptions)
    {
        return databaseType switch
        {
            "MsSql"      => new MsSqlDbService(connectionString, mapperOptions, serviceOptions),
            "MySql"      => new MySqlDbService(connectionString, mapperOptions, serviceOptions),
            "PostgreSQL" => new PostgreSqlDbService(connectionString, mapperOptions, serviceOptions),
            _ => throw new InvalidOperationException($"Unsupported database type '{databaseType}'. Supported types: MsSql, MySql, PostgreSQL."),
        };
    }
}

/// <summary>
/// Fluent builder for registering multiple named database connections.
/// </summary>
public class JadeDbNamedConnectionsBuilder
{
    private readonly Dictionary<string, (string DatabaseType, string ConnectionString)> _connections
        = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyDictionary<string, (string DatabaseType, string ConnectionString)> Connections => _connections;

    /// <summary>
    /// The name of the default connection, or <c>null</c> if no default has been set.
    /// </summary>
    public string? DefaultConnectionName { get; private set; }

    /// <summary>
    /// Registers a named database connection.
    /// </summary>
    /// <param name="name">A unique name for this connection (e.g. <c>"default"</c>, <c>"reports"</c>).</param>
    /// <param name="databaseType">The database type: <c>"MsSql"</c>, <c>"MySql"</c>, or <c>"PostgreSQL"</c>.</param>
    /// <param name="connectionString">The connection string for the database.</param>
    public JadeDbNamedConnectionsBuilder AddConnection(string name, string databaseType, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Connection name must not be null or whitespace.", nameof(name));
        if (string.IsNullOrWhiteSpace(databaseType)) throw new ArgumentException("Database type must not be null or whitespace.", nameof(databaseType));
        if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Connection string must not be null or whitespace.", nameof(connectionString));

        _connections[name] = (databaseType, connectionString);
        return this;
    }

    /// <summary>
    /// Designates the connection with the given <paramref name="name"/> as the default connection.
    /// The default connection is returned by <see cref="IJadeDbServiceFactory.GetService()"/> (no-arg overload)
    /// and is also registered directly as <see cref="IDatabaseService"/> in the DI container so it can be
    /// injected without touching the factory at all.
    /// </summary>
    /// <param name="name">The name of an already-registered connection to use as the default.</param>
    public JadeDbNamedConnectionsBuilder SetDefaultConnection(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Connection name must not be null or whitespace.", nameof(name));
        DefaultConnectionName = name;
        return this;
    }
}