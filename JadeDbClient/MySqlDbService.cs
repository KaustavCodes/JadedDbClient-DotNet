using System.Data;
using JadeDbClient.Interfaces;
using JadeDbClient.Initialize;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System.Diagnostics.CodeAnalysis;
using JadeDbClient.Helpers;
using System.Diagnostics;
using JadeDbClient.Enums;
using JadeDbClient.Services;
using System.Threading;
using System.Threading.Tasks;

namespace JadeDbClient;

public class MySqlDbService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly JadeDbMapperOptions _mapperOptions;
    private readonly JadeDbServiceRegistration.JadeDbServiceOptions _serviceOptions;
    private bool _disposed;

    public IDbConnection? Connection { get; set; }

    private readonly Mapper _mapper;

    // Backward compatible constructor (for existing users without logging)
    public MySqlDbService(IConfiguration configuration, JadeDbMapperOptions mapperOptions)
        : this(configuration, mapperOptions, new JadeDbServiceRegistration.JadeDbServiceOptions())
    {
    }

    public MySqlDbService(IConfiguration configuration, JadeDbMapperOptions mapperOptions, JadeDbServiceRegistration.JadeDbServiceOptions serviceOptions)
    {
        _connectionString = configuration["ConnectionStrings:DbConnection"]
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:DbConnection' not found in configuration.");
        _mapperOptions = mapperOptions ?? throw new ArgumentNullException(nameof(mapperOptions));
        _serviceOptions = serviceOptions ?? new JadeDbServiceRegistration.JadeDbServiceOptions(); // Default if null
        _mapper = new Mapper(_mapperOptions, _serviceOptions);
    }

    public MySqlDbService(string connectionString, JadeDbMapperOptions mapperOptions, JadeDbServiceRegistration.JadeDbServiceOptions serviceOptions)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _mapperOptions = mapperOptions ?? throw new ArgumentNullException(nameof(mapperOptions));
        _serviceOptions = serviceOptions ?? new JadeDbServiceRegistration.JadeDbServiceOptions();
        _mapper = new Mapper(_mapperOptions, _serviceOptions);
    }

    private void LogQueryExecution(string query, long elapsedMilliseconds)
    {
        if (_serviceOptions.EnableLogging)
        {
            if (_serviceOptions.LogExecutedQuery)
            {
                Console.WriteLine($"[JadeDbClient] [MYSQL] Executed Query: {query}");
            }
            Console.WriteLine($"[JadeDbClient] [MYSQL] Execution Time: {elapsedMilliseconds} ms");
        }
    }

    public DatabaseDialect Dialect => DatabaseDialect.MySql;

    public bool PluralizeTableNames => _serviceOptions.PluralizeTableName;

    /// <summary>
    /// Open a connection to the database
    /// </summary>
    public void OpenConnection()
    {
        if (Connection != null)
        {
            Connection.Dispose();
            Connection = null;
        }
        Connection = new MySqlConnection(_connectionString);
        Connection.Open();
    }

    /// <summary>
    /// Close the connection to the database
    /// </summary>
    public void CloseConnection()
    {
        if (Connection != null)
        {
            Connection.Dispose();
            Connection = null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            CloseConnection();
            _disposed = true;
        }
    }

    /// <inheritdoc/>
    public async Task<IDatabaseSession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return new DatabaseSession(
            connection,
            Dialect,
            PluralizeTableNames,
            _mapper,
            _mapperOptions,
            _serviceOptions,
            (name, value, dbType, direction, size) => GetParameter(name, value, dbType, direction, size));
    }

    /// <inheritdoc/>
    public IDatabaseSession OpenSession()
    {
        var connection = new MySqlConnection(_connectionString);
        connection.Open();
        return new DatabaseSession(
            connection,
            Dialect,
            PluralizeTableNames,
            _mapper,
            _mapperOptions,
            _serviceOptions,
            (name, value, dbType, direction, size) => GetParameter(name, value, dbType, direction, size));
    }

    /// <summary>
    /// Creates a new instance of an <see cref="IDbDataParameter"/> for MySql.
    /// </summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="value">The value of the parameter.</param>
    /// <param name="dbType">The <see cref="DbType"/> of the parameter.</param>
    /// <param name="direction">The <see cref="ParameterDirection"/> of the parameter. Default is <see cref="ParameterDirection.Input"/>.</param>
    /// <param name="size">The size of the parameter. Default is 0.</param>
    /// <returns>A new instance of <see cref="MySqlParameter"/> configured with the specified properties.</returns>
    public IDbDataParameter GetParameter(string name, object value, DbType dbType, ParameterDirection direction = ParameterDirection.Input, int size = 0)
    {
        return new MySqlParameter
        {
            ParameterName = name,
            Value = value,
            DbType = dbType,
            Direction = direction,
            Size = size
        };
    }

    /// <summary>
    /// Executes a SQL query asynchronously and maps the result to a collection of objects of type T.
    /// </summary>
    /// <typeparam name="T">The type of objects to which the query results will be mapped. The type T should have properties that match the column names in the query result.</typeparam>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the SQL query. Default is null.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a collection of objects of type T that represent the rows returned by the query.</returns>
    public async Task<IEnumerable<T>> ExecuteQueryAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        var results = new List<T>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(query, connection))
            {
                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (_mapperOptions.TryGetMapper<T>(out var mapper) && mapper != null)
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(mapper(reader));
                        }
                    }
                    else
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(_mapper.MapObject<T>(reader));
                        }
                    }
                }
            }

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution(query, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }
        }

        return results;
    }

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first result object of type T.
    /// </summary>
    /// <typeparam name="T">The type of objects to which the query results will be mapped. The type T should have properties that match the column names in the query result.</typeparam>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the SQL query. Default is null.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a collection of objects of type T that represent the rows returned by the query.</returns>
    public async Task<T?> ExecuteQueryFirstRowAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(query, connection))
            {
                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await reader.ReadAsync())
                    {
                        T? result;
                        if (_mapperOptions.TryGetMapper<T>(out var mapper) && mapper != null)
                            result = mapper(reader);
                        else
                            result = _mapper.MapObject<T>(reader);

                        if (_serviceOptions.EnableLogging)
                        {
                            LogQueryExecution(query, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
                        }
                        return result;
                    }
                }
            }

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution(query, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<dynamic>> ExecuteQueryDynamicAsync(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        var results = new List<dynamic>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(query, connection))
            {
                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                        command.Parameters.Add(parameter);
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                        results.Add(_mapper.MapDynamic(reader));
                }
            }

            if (_serviceOptions.EnableLogging)
                LogQueryExecution(query, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<dynamic?> ExecuteQueryFirstRowDynamicAsync(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(query, connection))
            {
                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                        command.Parameters.Add(parameter);
                }

                using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (await reader.ReadAsync())
                    {
                        var result = _mapper.MapDynamic(reader);
                        if (_serviceOptions.EnableLogging)
                            LogQueryExecution(query, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
                        return result;
                    }
                }
            }

            if (_serviceOptions.EnableLogging)
                LogQueryExecution(query, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }

        return null;
    }

    /// <summary>
    /// Executes a query and returns a single value (scalar) result.
    /// </summary>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="parameters">>A collection of parameters to be used in the SQL query. Default is null.</param>
    public async Task<T?> ExecuteScalar<T>(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(query, connection))
            {
                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                var data = await command.ExecuteScalarAsync();

                if (_serviceOptions.EnableLogging)
                {
                    LogQueryExecution(query, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
                }

                if (data != null && data != DBNull.Value)
                {
                    return (T)data;
                }
                else
                {
                    return default(T);
                }
            }
        }
    }

    /// <summary>
    /// Executes a stored procedure asynchronously and maps the result to a collection of objects of type T.
    /// </summary>
    /// <typeparam name="T">The type of objects to which the stored procedure results will be mapped. The type T should have properties that match the column names in the result set.</typeparam>
    /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the stored procedure. Default is null.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a collection of objects of type T that represent the rows returned by the stored procedure.</returns>
    public async Task<IEnumerable<T>> ExecuteStoredProcedureSelectDataAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null)
    {
        var results = new List<T>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(storedProcedureName, connection))
            {
                command.CommandType = CommandType.StoredProcedure;

                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (_mapperOptions.TryGetMapper<T>(out var mapper) && mapper != null)
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(mapper(reader));
                        }
                    }
                    else
                    {
                        while (await reader.ReadAsync())
                        {
                            results.Add(_mapper.MapObject<T>(reader));
                        }
                    }
                }
            }

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"StoredProcedure: {storedProcedureName}", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }
        }

        return results;
    }

    /// <summary>
    /// Executes a stored procedure asynchronously and returns the number of rows affected.
    /// </summary>
    /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the stored procedure. Default is null.</param>
    /// <returns>The number of rows effected after executing the stored procedure.</returns>
    public async Task<int> ExecuteStoredProcedureAsync(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null)
    {
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(storedProcedureName, connection))
            {
                command.CommandType = CommandType.StoredProcedure;

                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                int affectedRows = await command.ExecuteNonQueryAsync();

                if (_serviceOptions.EnableLogging)
                {
                    LogQueryExecution($"StoredProcedure: {storedProcedureName}", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
                }

                return affectedRows;
            }
        }
    }

    /// <summary>
    /// Executes a stored procedure asynchronously and retrieves the output parameters.
    /// </summary>
    /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the stored procedure. This includes input, output, and input-output parameters.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a dictionary where the keys are the names of the output parameters and the values are their corresponding values.</returns>
    public async Task<Dictionary<string, object>> ExecuteStoredProcedureWithOutputAsync(string storedProcedureName, IEnumerable<IDbDataParameter> parameters)
    {
        var outputValues = new Dictionary<string, object>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(storedProcedureName, connection))
            {
                command.CommandType = CommandType.StoredProcedure;

                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                await command.ExecuteNonQueryAsync();

                foreach (MySqlParameter parameter in command.Parameters)
                {
                    if (parameter.Direction == ParameterDirection.Output || parameter.Direction == ParameterDirection.InputOutput)
                    {
                        outputValues.Add(parameter.ParameterName, parameter.Value ?? DBNull.Value);
                    }
                }

                if (_serviceOptions.EnableLogging)
                {
                    LogQueryExecution($"StoredProcedureWithOutput: {storedProcedureName}", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
                }
            }
        }

        return outputValues;
    }

    /// <summary>
    /// Executes a SQL command asynchronously.
    /// </summary>
    /// <param name="commandText">The SQL command to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the SQL command. Default is null.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteCommandAsync(string commandText, IEnumerable<IDbDataParameter>? parameters = null)
    {
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new MySqlCommand(commandText, connection))
            {
                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                await command.ExecuteNonQueryAsync();
            }

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution(commandText, (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// Bulk inserts a DataTable into a MySql table.
    /// </summary>
    /// <param name="dataTable">The DataTable to insert.</param>
    /// <param name="tableName">The target MySql table name.</param>
    public async Task<bool> InsertDataTable(string tableName, DataTable dataTable)
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // Build the parameterized INSERT query
            var columns = dataTable.Columns.Cast<DataColumn>().Select(c => $"`{c.ColumnName}`").ToList();
            var columnList = string.Join(", ", columns);
            var paramPlaceholders = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
            var insertQuery = $"INSERT INTO `{tableName}` ({columnList}) VALUES ({paramPlaceholders})";

            foreach (DataRow row in dataTable.Rows)
            {
                using var command = new MySqlCommand(insertQuery, connection, transaction);

                // Add parameters for the current row
                for (int i = 0; i < columns.Count; i++)
                {
                    command.Parameters.AddWithValue($"@p{i}", row[i]);
                }

                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"InsertDataTable({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Bulk inserts a DataTable with JSON data into a MySQL table.
    /// </summary>
    /// <param name="dataTable">The DataTable to insert.</param>
    /// <param name="tableName">The target MySQL table name.</param>
    /// <remarks>
    /// Supports System.Text.Json types (JsonElement) and Newtonsoft.Json types (JObject) for backward compatibility.
    /// </remarks>
    public async Task<bool> InsertDataTableWithJsonData(string tableName, DataTable dataTable)
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var columns = dataTable.Columns.Cast<DataColumn>().Select(c => $"`{c.ColumnName}`").ToList();
            var columnList = string.Join(", ", columns);
            var paramPlaceholders = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
            var insertQuery = $"INSERT INTO `{tableName}` ({columnList}) VALUES ({paramPlaceholders})";

            foreach (DataRow row in dataTable.Rows)
            {
                using var command = new MySqlCommand(insertQuery, connection, transaction);

                for (int i = 0; i < columns.Count; i++)
                {
                    var item = row[i];
                    if (item == null || item == DBNull.Value)
                    {
                        command.Parameters.AddWithValue($"@p{i}", DBNull.Value);
                    }
                    else if (item is Newtonsoft.Json.Linq.JObject jObj)
                    {
                        // Use parameterless ToString() for AOT compatibility (produces formatted JSON, still valid)
                        command.Parameters.AddWithValue($"@p{i}", jObj.ToString());
                    }
                    else if (item is System.Text.Json.JsonElement jsonElement)
                    {
                        command.Parameters.AddWithValue($"@p{i}", jsonElement.GetRawText());
                    }
                    else
                    {
                        command.Parameters.AddWithValue($"@p{i}", item);
                    }
                }

                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"InsertDataTableWithJsonData({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }

            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Bulk inserts a collection of objects into a MySQL table using optimized batch inserts.
    /// </summary>
    /// <typeparam name="T">The type of objects to insert. Properties should match database column names.</typeparam>
    /// <param name="tableName">The target database table name.</param>
    /// <param name="items">The collection of items to insert.</param>
    /// <param name="batchSize">Number of records to insert per batch (default 1000).</param>
    /// <returns>The total number of rows inserted.</returns>
    public async Task<int> BulkInsertAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string tableName, IEnumerable<T> items, int batchSize = 1000)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));

        long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
        int totalInserted = 0;

        // Try to use generated accessor for reflection-free operation
        if (JadeDbMapperOptions.TryGetBulkInsertAccessor<T>(out var accessor) && accessor != null)
        {
            if (_serviceOptions.EnableLogging)
            {
                Console.WriteLine($"[BULK INSERT] Using SOURCE GENERATOR accessor for {typeof(T).Name}");
            }
            totalInserted = await BulkInsertWithAccessorAsync(tableName, items, accessor, batchSize);
            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"[JadeDbClient] BulkInsert<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }
            return totalInserted;
        }

        // Fallback to reflection-based approach
        if (_serviceOptions.EnableLogging)
        {
            Console.WriteLine($"[BULK INSERT] Falling back to REFLECTION for {typeof(T).Name}");
        }
        var properties = typeof(T).GetProperties().Where(p => p.CanRead).ToArray();
        if (properties.Length == 0) throw new InvalidOperationException($"Type {typeof(T).Name} has no readable properties");

        var columnNames = ReflectionHelper.GetColumnNames(properties).Select(c => $"`{c}`").ToArray();

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var batch = new List<T>();
            foreach (var item in items)
            {
                batch.Add(item);

                if (batch.Count >= batchSize)
                {
                    await InsertBatchAsync(connection, transaction, tableName, columnNames, properties, batch);
                    totalInserted += batch.Count;
                    batch.Clear();
                }
            }

            // Insert remaining items
            if (batch.Count > 0)
            {
                await InsertBatchAsync(connection, transaction, tableName, columnNames, properties, batch);
                totalInserted += batch.Count;
            }

            await transaction.CommitAsync();

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"[JadeDbClient] BulkInsert<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }

            return totalInserted;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Reflection-free bulk insert using generated accessor
    /// </summary>
    private async Task<int> BulkInsertWithAccessorAsync<T>(string tableName, IEnumerable<T> items, BulkInsertAccessor accessor, int batchSize)
    {
        long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
        var columnNames = accessor.ColumnNames.Select(c => $"`{c}`").ToArray();
        int totalInserted = 0;

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var batch = new List<T>();
            foreach (var item in items)
            {
                batch.Add(item);

                if (batch.Count >= batchSize)
                {
                    await InsertBatchWithAccessorAsync(connection, transaction, tableName, columnNames, accessor, batch);
                    totalInserted += batch.Count;
                    batch.Clear();
                }
            }

            // Insert remaining items
            if (batch.Count > 0)
            {
                await InsertBatchWithAccessorAsync(connection, transaction, tableName, columnNames, accessor, batch);
                totalInserted += batch.Count;
            }

            await transaction.CommitAsync();

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"[JadeDbClient] BulkInsertWithAccessor<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }

            return totalInserted;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Helper method to insert a batch using accessor (reflection-free)
    /// </summary>
    private async Task InsertBatchWithAccessorAsync<T>(MySqlConnection connection, MySqlTransaction transaction,
        string tableName, string[] columnNames, BulkInsertAccessor accessor, List<T> batch)
    {
        if (batch.Count == 0) return;

        var columnList = string.Join(", ", columnNames);
        var valuesList = new List<string>();
        var parameters = new List<MySqlParameter>();

        for (int i = 0; i < batch.Count; i++)
        {
            var values = accessor.GetValues(batch[i]!);
            var paramPlaceholders = new List<string>();
            for (int j = 0; j < values.Length; j++)
            {
                var paramName = $"@p{i}_{j}";
                paramPlaceholders.Add(paramName);
                parameters.Add(new MySqlParameter(paramName, values[j] ?? DBNull.Value));
            }
            valuesList.Add($"({string.Join(", ", paramPlaceholders)})");
        }

        var insertQuery = $"INSERT INTO `{tableName}` ({columnList}) VALUES {string.Join(", ", valuesList)}";

        using var command = new MySqlCommand(insertQuery, connection, transaction);
        command.Parameters.AddRange(parameters.ToArray());
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Bulk inserts a stream of objects into a MySQL table with progress reporting.
    /// </summary>
    /// <typeparam name="T">The type of objects to insert. Properties should match database column names.</typeparam>
    /// <param name="tableName">The target database table name.</param>
    /// <param name="items">The async enumerable stream of items to insert.</param>
    /// <param name="progress">Optional progress reporter that receives the count of rows inserted.</param>
    /// <param name="batchSize">Number of records to insert per batch (default 1000).</param>
    /// <returns>The total number of rows inserted.</returns>
    public async Task<int> BulkInsertAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string tableName, IAsyncEnumerable<T> items, IProgress<int>? progress = null, int batchSize = 1000)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));

        long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
        int totalInserted = 0;

        // Try to use generated accessor for reflection-free operation
        if (JadeDbMapperOptions.TryGetBulkInsertAccessor<T>(out var accessor) && accessor != null)
        {
            if (_serviceOptions.EnableLogging)
            {
                Console.WriteLine($"[BULK INSERT STREAM] Using SOURCE GENERATOR accessor for {typeof(T).Name}");
            }
            totalInserted = await BulkInsertWithAccessorAsync(tableName, items, accessor, progress, batchSize);
            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"[JadeDbClient] BulkInsertStream<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }
            return totalInserted;
        }

        // Fallback to reflection-based approach
        if (_serviceOptions.EnableLogging)
        {
            Console.WriteLine($"[BULK INSERT STREAM] Falling back to REFLECTION for {typeof(T).Name}");
        }
        var properties = typeof(T).GetProperties().Where(p => p.CanRead).ToArray();
        if (properties.Length == 0) throw new InvalidOperationException($"Type {typeof(T).Name} has no readable properties");

        var columnNames = ReflectionHelper.GetColumnNames(properties).Select(c => $"`{c}`").ToArray();

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var batch = new List<T>();
            await foreach (var item in items)
            {
                batch.Add(item);

                if (batch.Count >= batchSize)
                {
                    await InsertBatchAsync(connection, transaction, tableName, columnNames, properties, batch);
                    totalInserted += batch.Count;
                    progress?.Report(totalInserted);
                    batch.Clear();
                }
            }

            // Insert remaining items
            if (batch.Count > 0)
            {
                await InsertBatchAsync(connection, transaction, tableName, columnNames, properties, batch);
                totalInserted += batch.Count;
                progress?.Report(totalInserted);
            }

            await transaction.CommitAsync();

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"[JadeDbClient] BulkInsertStream<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }

            return totalInserted;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Reflection-free bulk insert using generated accessor with progress reporting
    /// </summary>
    private async Task<int> BulkInsertWithAccessorAsync<T>(string tableName, IAsyncEnumerable<T> items, BulkInsertAccessor accessor, IProgress<int>? progress, int batchSize)
    {
        long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
        var columnNames = accessor.ColumnNames.Select(c => $"`{c}`").ToArray();
        int totalInserted = 0;

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var batch = new List<T>();
            await foreach (var item in items)
            {
                batch.Add(item);

                if (batch.Count >= batchSize)
                {
                    await InsertBatchWithAccessorAsync(connection, transaction, tableName, columnNames, accessor, batch);
                    totalInserted += batch.Count;
                    progress?.Report(totalInserted);
                    batch.Clear();
                }
            }

            // Insert remaining items
            if (batch.Count > 0)
            {
                await InsertBatchWithAccessorAsync(connection, transaction, tableName, columnNames, accessor, batch);
                totalInserted += batch.Count;
                progress?.Report(totalInserted);
            }

            await transaction.CommitAsync();

            if (_serviceOptions.EnableLogging)
            {
                LogQueryExecution($"[JadeDbClient] BulkInsertWithAccessorStream<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
            }

            return totalInserted;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Helper method to insert a batch of items using multi-value INSERT statement.
    /// </summary>
    private async Task InsertBatchAsync<T>(MySqlConnection connection, MySqlTransaction transaction,
        string tableName, string[] columnNames, System.Reflection.PropertyInfo[] properties, List<T> batch)
    {
        if (batch.Count == 0) return;

        var columnList = string.Join(", ", columnNames);
        var valuesList = new List<string>();
        var parameters = new List<MySqlParameter>();

        for (int i = 0; i < batch.Count; i++)
        {
            var paramPlaceholders = new List<string>();
            for (int j = 0; j < properties.Length; j++)
            {
                var paramName = $"@p{i}_{j}";
                paramPlaceholders.Add(paramName);
                var value = properties[j].GetValue(batch[i]);
                parameters.Add(new MySqlParameter(paramName, value ?? DBNull.Value));
            }
            valuesList.Add($"({string.Join(", ", paramPlaceholders)})");
        }

        var insertQuery = $"INSERT INTO `{tableName}` ({columnList}) VALUES {string.Join(", ", valuesList)}";

        using var command = new MySqlCommand(insertQuery, connection, transaction);
        command.Parameters.AddRange(parameters.ToArray());
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Begins a database transaction.
    /// </summary>
    /// <returns>An IDbTransaction object representing the new transaction.</returns>
    public IDbTransaction BeginTransaction()
    {
        if (Connection == null || Connection.State != ConnectionState.Open)
        {
            OpenConnection();
        }
        return Connection!.BeginTransaction();
    }

    /// <summary>
    /// Begins a database transaction with the specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction.</param>
    /// <returns>An IDbTransaction object representing the new transaction.</returns>
    public IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        if (Connection == null || Connection.State != ConnectionState.Open)
        {
            OpenConnection();
        }
        return Connection!.BeginTransaction(isolationLevel);
    }

    /// <summary>
    /// Commits the current database transaction.
    /// </summary>
    /// <param name="transaction">The transaction to commit.</param>
    public void CommitTransaction(IDbTransaction transaction)
    {
        if (transaction == null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }
        transaction.Commit();
    }

    /// <summary>
    /// Rolls back the current database transaction.
    /// </summary>
    /// <param name="transaction">The transaction to roll back.</param>
    public void RollbackTransaction(IDbTransaction transaction)
    {
        if (transaction == null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }
        transaction.Rollback();
    }
}