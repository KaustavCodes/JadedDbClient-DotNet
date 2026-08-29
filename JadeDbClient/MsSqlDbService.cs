using System.Data;
using JadeDbClient.Interfaces;
using JadeDbClient.Initialize;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using System.Diagnostics.CodeAnalysis;
using JadeDbClient.Helpers;
using System.Diagnostics;
using JadeDbClient.Enums;
using JadeDbClient.Services;
using System.Threading;
using System.Threading.Tasks;

namespace JadeDbClient;

public class MsSqlDbService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly JadeDbMapperOptions _mapperOptions;
    private readonly JadeDbServiceRegistration.JadeDbServiceOptions _serviceOptions;
    private bool _disposed;

    public IDbConnection? Connection { get; set; }

    private readonly Mapper _mapper;

    // Backward compatible constructor (for existing users without logging)
    public MsSqlDbService(IConfiguration configuration, JadeDbMapperOptions mapperOptions)
        : this(configuration, mapperOptions, new JadeDbServiceRegistration.JadeDbServiceOptions())
    {
    }

    public MsSqlDbService(IConfiguration configuration, JadeDbMapperOptions mapperOptions, JadeDbServiceRegistration.JadeDbServiceOptions serviceOptions)
    {
        _connectionString = configuration["ConnectionStrings:DbConnection"]
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:DbConnection' not found in configuration.");
        _mapperOptions = mapperOptions ?? throw new ArgumentNullException(nameof(mapperOptions));
        _serviceOptions = serviceOptions ?? new JadeDbServiceRegistration.JadeDbServiceOptions(); // Default if null
        _mapper = new Mapper(_mapperOptions, _serviceOptions);
    }

    public MsSqlDbService(string connectionString, JadeDbMapperOptions mapperOptions, JadeDbServiceRegistration.JadeDbServiceOptions serviceOptions)
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
                Console.WriteLine($"[JadeDbClient] [MSSQL] Executed Query: {query}");
            }
            Console.WriteLine($"[JadeDbClient] [MSSQL] Execution Time: {elapsedMilliseconds} ms");
        }
    }

    public DatabaseDialect Dialect => DatabaseDialect.MsSql;

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
        Connection = new SqlConnection(_connectionString);
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
        var connection = new SqlConnection(_connectionString);
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
        var connection = new SqlConnection(_connectionString);
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
    /// Creates a new instance of an <see cref="IDbDataParameter"/> for SQL Server.
    /// </summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="value">The value of the parameter.</param>
    /// <param name="dbType">The <see cref="DbType"/> of the parameter.</param>
    /// <param name="direction">The <see cref="ParameterDirection"/> of the parameter. Default is <see cref="ParameterDirection.Input"/>.</param>
    /// <param name="size">The size of the parameter. Default is 0.</param>
    /// <returns>A new instance of <see cref="SqlParameter"/> configured with the specified properties.</returns>
    public IDbDataParameter GetParameter(string name, object value, DbType dbType, ParameterDirection direction = ParameterDirection.Input, int size = 0)
    {
        return new SqlParameter
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
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">Thrown when there is an error executing the query.</exception>
    /// <exception cref="InvalidOperationException">Thrown when there is an error creating an instance of type T.</exception>
    /// <exception cref="ArgumentException">Thrown when there is an error setting a property value.</exception>
    public async Task<IEnumerable<T>> ExecuteQueryAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        var results = new List<T>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
            using (var command = new SqlCommand(query, connection))
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
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">Thrown when there is an error executing the query.</exception>
    /// <exception cref="InvalidOperationException">Thrown when there is an error creating an instance of type T.</exception>
    /// <exception cref="ArgumentException">Thrown when there is an error setting a property value.</exception>
    public async Task<T?> ExecuteQueryFirstRowAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
            using (var command = new SqlCommand(query, connection))
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

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
            using (var command = new SqlCommand(query, connection))
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
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
            using (var command = new SqlCommand(query, connection))
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
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
            using (var command = new SqlCommand(query, connection))
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
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">Thrown when there is an error executing the stored procedure.</exception>
    /// <exception cref="InvalidOperationException">Thrown when there is an error creating an instance of type T.</exception>
    /// <exception cref="ArgumentException">Thrown when there is an error setting a property value.</exception>
    public async Task<IEnumerable<T>> ExecuteStoredProcedureSelectDataAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null)
    {
        var results = new List<T>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var stopwatch = Stopwatch.StartNew();
            using (var command = new SqlCommand(storedProcedureName, connection))
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
            stopwatch.Stop();
            LogQueryExecution($"StoredProcedure: {storedProcedureName}", stopwatch.ElapsedMilliseconds);
        }

        return results;
    }

    /// <summary>
    /// Executes a stored procedure asynchronously and returns the number of rows affected.
    /// </summary>
    /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the stored procedure. Default is null.</param>
    /// <returns>The number of rows effected after executing the stored procedure.</returns>
    /// <exception cref="SqlException">Thrown when there is an error executing the stored procedure.</exception>
    public async Task<int> ExecuteStoredProcedureAsync(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new SqlCommand(storedProcedureName, connection))
            {
                command.CommandType = CommandType.StoredProcedure;

                if (parameters != null)
                {
                    foreach (var parameter in parameters)
                    {
                        command.Parameters.Add(parameter);
                    }
                }

                var affectedRows = await command.ExecuteNonQueryAsync();

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
    /// <exception cref="SqlException">Thrown when there is an error executing the stored procedure.</exception>
    public async Task<Dictionary<string, object>> ExecuteStoredProcedureWithOutputAsync(string storedProcedureName, IEnumerable<IDbDataParameter> parameters)
    {
        var outputValues = new Dictionary<string, object>();

        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            var stopwatch = Stopwatch.StartNew();
            using (var command = new SqlCommand(storedProcedureName, connection))
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

                foreach (SqlParameter parameter in command.Parameters)
                {
                    if (parameter.Direction == ParameterDirection.Output || parameter.Direction == ParameterDirection.InputOutput)
                    {
                        outputValues.Add(parameter.ParameterName, parameter.Value ?? DBNull.Value);
                    }
                }

                stopwatch.Stop();
                LogQueryExecution($"StoredProcedureWithOutput: {storedProcedureName}", stopwatch.ElapsedMilliseconds);
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
    /// <exception cref="NpgsqlException">Thrown when there is an error executing the command.</exception>
    public async Task ExecuteCommandAsync(string commandText, IEnumerable<IDbDataParameter>? parameters = null)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;

            using (var command = new SqlCommand(commandText, connection))
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
    /// Bulk inserts a DataTable into a PostgreSQL table.
    /// </summary>
    /// <param name="dataTable">The DataTable to insert.</param>
    /// <param name="tableName">The target PostgreSQL table name.</param>
    public async Task<bool> InsertDataTable(string tableName, DataTable dataTable)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using (var bulkCopy = new SqlBulkCopy(connection))
        {
            // Set the destination table name
            bulkCopy.DestinationTableName = tableName;

            // Map columns from the DataTable to the SQL Server table
            foreach (DataColumn column in dataTable.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            // Perform the bulk copy
            await bulkCopy.WriteToServerAsync(dataTable);
        }

        return true;
    }

    /// <summary>
    /// Bulk inserts a DataTable with JSON data into a SQL Server table.
    /// </summary>
    /// <param name="dataTable">The DataTable to insert.</param>
    /// <param name="tableName">The target SQL Server table name.</param>
    /// <remarks>
    /// Supports System.Text.Json types (JsonElement) and Newtonsoft.Json types (JObject) for backward compatibility.
    /// </remarks>
    public async Task<bool> InsertDataTableWithJsonData(string tableName, DataTable dataTable)
    {
        // Clone the structure and copy data, serializing JSON objects as needed
        var processedTable = dataTable.Clone();

        foreach (DataRow row in dataTable.Rows)
        {
            var newRow = processedTable.NewRow();
            for (int i = 0; i < dataTable.Columns.Count; i++)
            {
                var item = row[i];
                if (item == null || item == DBNull.Value)
                {
                    newRow[i] = DBNull.Value;
                }
                else if (item is Newtonsoft.Json.Linq.JObject jObj)
                {
                    // Use parameterless ToString() for AOT compatibility (produces formatted JSON, still valid)
                    newRow[i] = jObj.ToString();
                }
                else if (item is System.Text.Json.JsonElement jsonElement)
                {
                    newRow[i] = jsonElement.GetRawText();
                }
                else
                {
                    newRow[i] = item;
                }
            }
            processedTable.Rows.Add(newRow);
        }

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using (var bulkCopy = new SqlBulkCopy(connection))
        {
            bulkCopy.DestinationTableName = tableName;
            foreach (DataColumn column in processedTable.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }
            await bulkCopy.WriteToServerAsync(processedTable);
        }

        return true;
    }

    /// <summary>
    /// Bulk inserts a collection of objects into a SQL Server table using SqlBulkCopy.
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

        // Create DataTable structure once
        var dataTable = new DataTable();
        foreach (var property in properties)
        {
            var columnType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            dataTable.Columns.Add(property.Name, columnType);
        }

        int batchCount = 0;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var item in items)
        {
            var row = dataTable.NewRow();
            foreach (var property in properties)
            {
                var value = property.GetValue(item);
                row[property.Name] = value ?? DBNull.Value;
            }
            dataTable.Rows.Add(row);
            batchCount++;

            // Insert batch when size is reached
            if (batchCount >= batchSize)
            {
                await ExecuteSqlBulkCopyAsync(connection, tableName, dataTable, properties, batchSize);
                totalInserted += batchCount;
                dataTable.Rows.Clear();
                batchCount = 0;
            }
        }

        // Insert remaining items
        if (batchCount > 0)
        {
            await ExecuteSqlBulkCopyAsync(connection, tableName, dataTable, properties, batchSize);
            totalInserted += batchCount;
        }

        if (_serviceOptions.EnableLogging)
        {
            LogQueryExecution($"[JadeDbClient] BulkInsert<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }

        return totalInserted;
    }

    /// <summary>
    /// Reflection-free bulk insert using generated accessor
    /// </summary>
    private async Task<int> BulkInsertWithAccessorAsync<T>(string tableName, IEnumerable<T> items, BulkInsertAccessor accessor, int batchSize)
    {
        long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
        // Create DataTable structure based on accessor columns
        var dataTable = new DataTable();
        foreach (var columnName in accessor.ColumnNames)
        {
            dataTable.Columns.Add(columnName, typeof(object)); // Use object type for flexibility
        }

        int totalInserted = 0;
        int batchCount = 0;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var item in items)
        {
            var row = dataTable.NewRow();
            var values = accessor.GetValues(item!);
            for (int i = 0; i < values.Length; i++)
            {
                row[i] = values[i] ?? DBNull.Value;
            }
            dataTable.Rows.Add(row);
            batchCount++;

            // Insert batch when size is reached
            if (batchCount >= batchSize)
            {
                await ExecuteSqlBulkCopyWithAccessorAsync(connection, tableName, dataTable, accessor.ColumnNames, batchSize);
                totalInserted += batchCount;
                dataTable.Rows.Clear();
                batchCount = 0;
            }
        }

        // Insert remaining items
        if (batchCount > 0)
        {
            await ExecuteSqlBulkCopyWithAccessorAsync(connection, tableName, dataTable, accessor.ColumnNames, batchSize);
            totalInserted += batchCount;
        }

        if (_serviceOptions.EnableLogging)
        {
            LogQueryExecution($"[JadeDbClient] BulkInsertWithAccessor<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }

        return totalInserted;
    }

    /// <summary>
    /// Helper method for SqlBulkCopy with accessor (column names only)
    /// </summary>
    private async Task ExecuteSqlBulkCopyWithAccessorAsync(SqlConnection connection, string tableName,
        DataTable dataTable, string[] columnNames, int batchSize)
    {
        using var bulkCopy = new SqlBulkCopy(connection);
        bulkCopy.DestinationTableName = tableName;
        bulkCopy.BatchSize = batchSize;

        foreach (var columnName in columnNames)
        {
            bulkCopy.ColumnMappings.Add(columnName, columnName);
        }

        await bulkCopy.WriteToServerAsync(dataTable);
    }

    /// <summary>
    /// Bulk inserts a stream of objects into a SQL Server table with progress reporting.
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

        // Create DataTable structure
        var dataTable = new DataTable();
        foreach (var property in properties)
        {
            var columnType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            dataTable.Columns.Add(property.Name, columnType);
        }

        int batchCount = 0;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await foreach (var item in items)
        {
            var row = dataTable.NewRow();
            foreach (var property in properties)
            {
                var value = property.GetValue(item);
                row[property.Name] = value ?? DBNull.Value;
            }
            dataTable.Rows.Add(row);
            batchCount++;

            // Insert batch when size is reached
            if (batchCount >= batchSize)
            {
                await ExecuteSqlBulkCopyAsync(connection, tableName, dataTable, properties, batchSize);
                totalInserted += batchCount;
                progress?.Report(totalInserted);

                dataTable.Rows.Clear();
                batchCount = 0;
            }
        }

        // Insert remaining items
        if (batchCount > 0)
        {
            await ExecuteSqlBulkCopyAsync(connection, tableName, dataTable, properties, batchSize);
            totalInserted += batchCount;
            progress?.Report(totalInserted);
        }

        if (_serviceOptions.EnableLogging)
        {
            LogQueryExecution($"[JadeDbClient] BulkInsertStream<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }

        return totalInserted;
    }

    /// <summary>
    /// Reflection-free bulk insert using generated accessor with progress reporting
    /// </summary>
    private async Task<int> BulkInsertWithAccessorAsync<T>(string tableName, IAsyncEnumerable<T> items, BulkInsertAccessor accessor, IProgress<int>? progress, int batchSize)
    {
        long startTimestamp = _serviceOptions.EnableLogging ? Stopwatch.GetTimestamp() : 0;
        // Create DataTable structure based on accessor columns
        var dataTable = new DataTable();
        foreach (var columnName in accessor.ColumnNames)
        {
            dataTable.Columns.Add(columnName, typeof(object));
        }

        int totalInserted = 0;
        int batchCount = 0;

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await foreach (var item in items)
        {
            var row = dataTable.NewRow();
            var values = accessor.GetValues(item!);
            for (int i = 0; i < values.Length; i++)
            {
                row[i] = values[i] ?? DBNull.Value;
            }
            dataTable.Rows.Add(row);
            batchCount++;

            // Insert batch when size is reached
            if (batchCount >= batchSize)
            {
                await ExecuteSqlBulkCopyWithAccessorAsync(connection, tableName, dataTable, accessor.ColumnNames, batchSize);
                totalInserted += batchCount;
                progress?.Report(totalInserted);

                dataTable.Rows.Clear();
                batchCount = 0;
            }
        }

        // Insert remaining items
        if (batchCount > 0)
        {
            await ExecuteSqlBulkCopyWithAccessorAsync(connection, tableName, dataTable, accessor.ColumnNames, batchSize);
            totalInserted += batchCount;
            progress?.Report(totalInserted);
        }

        if (_serviceOptions.EnableLogging)
        {
            LogQueryExecution($"[JadeDbClient] BulkInsertWithAccessorStream<{typeof(T).Name}>({tableName})", (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);
        }

        return totalInserted;
    }

    /// <summary>
    /// Helper method to configure and execute SqlBulkCopy operation.
    /// </summary>
    private async Task ExecuteSqlBulkCopyAsync(SqlConnection connection, string tableName,
        DataTable dataTable, System.Reflection.PropertyInfo[] properties, int batchSize)
    {
        using var bulkCopy = new SqlBulkCopy(connection);
        bulkCopy.DestinationTableName = tableName;
        bulkCopy.BatchSize = batchSize;

        foreach (var property in properties)
        {
            var columnName = ReflectionHelper.GetColumnName(property);
            bulkCopy.ColumnMappings.Add(property.Name, columnName);
        }

        await bulkCopy.WriteToServerAsync(dataTable);
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