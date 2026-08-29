using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Threading;
using System.Threading.Tasks;
using JadeDbClient.Enums;
using JadeDbClient.Helpers;
using JadeDbClient.Initialize;
using JadeDbClient.Interfaces;

namespace JadeDbClient.Services;

internal class DatabaseSession : IDatabaseSession
{
    private readonly IDbConnection _connection;
    private readonly DatabaseDialect _dialect;
    private readonly bool _pluralizeTableNames;
    private readonly Mapper _mapper;
    private readonly JadeDbMapperOptions _mapperOptions;
    private readonly JadeDbServiceRegistration.JadeDbServiceOptions? _serviceOptions;
    private readonly Func<string, object, DbType, ParameterDirection, int, IDbDataParameter> _parameterFactory;
    private readonly Dictionary<string, DbCommand> _commandCache = new(StringComparer.Ordinal);
    private IDbTransaction? _transaction;
    private bool _disposed;

    public DatabaseSession(
        IDbConnection connection,
        DatabaseDialect dialect,
        bool pluralizeTableNames,
        Mapper mapper,
        JadeDbMapperOptions mapperOptions,
        JadeDbServiceRegistration.JadeDbServiceOptions? serviceOptions,
        Func<string, object, DbType, ParameterDirection, int, IDbDataParameter> parameterFactory)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _dialect = dialect;
        _pluralizeTableNames = pluralizeTableNames;
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _mapperOptions = mapperOptions ?? throw new ArgumentNullException(nameof(mapperOptions));
        _serviceOptions = serviceOptions;
        _parameterFactory = parameterFactory ?? throw new ArgumentNullException(nameof(parameterFactory));
    }

    public IDbConnection Connection => _connection;
    public IDbTransaction? Transaction => _transaction;
    public DatabaseDialect Dialect => _dialect;
    public bool PluralizeTableNames => _pluralizeTableNames;

    public IDbTransaction BeginTransaction()
    {
        EnsureConnected();
        _transaction = _connection.BeginTransaction();
        return _transaction;
    }

    public IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        EnsureConnected();
        _transaction = _connection.BeginTransaction(isolationLevel);
        return _transaction;
    }

    public void CommitTransaction()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to commit.");
        _transaction.Commit();
        _transaction.Dispose();
        _transaction = null;
    }

    public void RollbackTransaction()
    {
        if (_transaction == null)
            throw new InvalidOperationException("No active transaction to rollback.");
        _transaction.Rollback();
        _transaction.Dispose();
        _transaction = null;
    }

    public async Task<IEnumerable<T>> ExecuteQueryAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        var (command, isCached) = await PrepareOrGetCommandAsync(query, parameters);
        try
        {
            var results = new List<T>();
            using var reader = await command.ExecuteReaderAsync();
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

            LogIfEnabled(query, startTimestamp);
            return results;
        }
        finally
        {
            if (!isCached)
            {
                command.Dispose();
            }
        }
    }

    public async Task<IEnumerable<dynamic>> ExecuteQueryDynamicAsync(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        var (command, isCached) = await PrepareOrGetCommandAsync(query, parameters);
        try
        {
            var results = new List<dynamic>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(MapRowToDynamic(reader));
            }

            LogIfEnabled(query, startTimestamp);
            return results;
        }
        finally
        {
            if (!isCached)
            {
                command.Dispose();
            }
        }
    }

    public async Task<T?> ExecuteQueryFirstRowAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        var (command, isCached) = await PrepareOrGetCommandAsync(query, parameters);
        try
        {
            T? result = default;
            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (await reader.ReadAsync())
            {
                if (_mapperOptions.TryGetMapper<T>(out var mapper) && mapper != null)
                    result = mapper(reader);
                else
                    result = _mapper.MapObject<T>(reader);
            }

            LogIfEnabled(query, startTimestamp);
            return result;
        }
        finally
        {
            if (!isCached)
            {
                command.Dispose();
            }
        }
    }

    public async Task<dynamic?> ExecuteQueryFirstRowDynamicAsync(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        var (command, isCached) = await PrepareOrGetCommandAsync(query, parameters);
        try
        {
            dynamic? result = null;
            using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (await reader.ReadAsync())
            {
                result = MapRowToDynamic(reader);
            }

            LogIfEnabled(query, startTimestamp);
            return result;
        }
        finally
        {
            if (!isCached)
            {
                command.Dispose();
            }
        }
    }

    public async Task<T?> ExecuteScalar<T>(string query, IEnumerable<IDbDataParameter>? parameters = null)
    {
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        var (command, isCached) = await PrepareOrGetCommandAsync(query, parameters);
        try
        {
            object? result = await command.ExecuteScalarAsync();
            LogIfEnabled(query, startTimestamp);

            if (result == null || result == DBNull.Value)
                return default;

            return (T)Convert.ChangeType(result, typeof(T));
        }
        finally
        {
            if (!isCached)
            {
                command.Dispose();
            }
        }
    }

    public async Task<int> ExecuteCommandAsync(string command, IEnumerable<IDbDataParameter>? parameters = null)
    {
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        var (cmd, isCached) = await PrepareOrGetCommandAsync(command, parameters);
        try
        {
            int rows = await cmd.ExecuteNonQueryAsync();
            LogIfEnabled(command, startTimestamp);
            return rows;
        }
        finally
        {
            if (!isCached)
            {
                cmd.Dispose();
            }
        }
    }

    public async Task<int> ExecuteStoredProcedureAsync(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null)
    {
        EnsureConnected();
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        using var command = (DbCommand)_connection.CreateCommand();
        command.CommandText = storedProcedureName;
        command.CommandType = CommandType.StoredProcedure;
        if (_transaction != null) command.Transaction = (DbTransaction)_transaction;
        AttachParameters(command, parameters);

        int rows = await command.ExecuteNonQueryAsync();
        LogIfEnabled(storedProcedureName, startTimestamp);
        return rows;
    }

    public async Task<IEnumerable<T>> ExecuteStoredProcedureSelectDataAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null)
    {
        EnsureConnected();
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        using var command = (DbCommand)_connection.CreateCommand();
        command.CommandText = storedProcedureName;
        command.CommandType = CommandType.StoredProcedure;
        if (_transaction != null) command.Transaction = (DbTransaction)_transaction;
        AttachParameters(command, parameters);

        var results = new List<T>();
        using var reader = await command.ExecuteReaderAsync();
        if (_mapperOptions.TryGetMapper<T>(out var mapper) && mapper != null)
        {
            while (await reader.ReadAsync()) results.Add(mapper(reader));
        }
        else
        {
            while (await reader.ReadAsync()) results.Add(_mapper.MapObject<T>(reader));
        }

        LogIfEnabled(storedProcedureName, startTimestamp);
        return results;
    }

    public async Task<Dictionary<string, object>> ExecuteStoredProcedureWithOutputAsync(string storedProcedureName, IEnumerable<IDbDataParameter> parameters)
    {
        EnsureConnected();
        long startTimestamp = _serviceOptions?.EnableLogging == true ? Stopwatch.GetTimestamp() : 0;

        using var command = (DbCommand)_connection.CreateCommand();
        command.CommandText = storedProcedureName;
        command.CommandType = CommandType.StoredProcedure;
        if (_transaction != null) command.Transaction = (DbTransaction)_transaction;
        AttachParameters(command, parameters);

        await command.ExecuteNonQueryAsync();

        var outputParams = new Dictionary<string, object>();
        foreach (IDbDataParameter param in command.Parameters)
        {
            if (param.Direction == ParameterDirection.Output || param.Direction == ParameterDirection.InputOutput)
            {
                outputParams[param.ParameterName] = param.Value ?? DBNull.Value;
            }
        }

        LogIfEnabled(storedProcedureName, startTimestamp);
        return outputParams;
    }

    public IDbDataParameter GetParameter(string name, object value, DbType dbType, ParameterDirection direction = ParameterDirection.Input, int size = 0)
    {
        return _parameterFactory(name, value, dbType, direction, size);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var cmd in _commandCache.Values)
        {
            cmd.Dispose();
        }
        _commandCache.Clear();
        _transaction?.Dispose();
        _transaction = null;
        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var cmd in _commandCache.Values)
        {
            await cmd.DisposeAsync();
        }
        _commandCache.Clear();
        if (_transaction is IAsyncDisposable asyncTx)
            await asyncTx.DisposeAsync();
        else
            _transaction?.Dispose();
        _transaction = null;

        if (_connection is IAsyncDisposable asyncConn)
            await asyncConn.DisposeAsync();
        else
            _connection.Dispose();
    }

    private void EnsureConnected()
    {
        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    private async ValueTask<(DbCommand Command, bool IsCached)> PrepareOrGetCommandAsync(string query, IEnumerable<IDbDataParameter>? parameters)
    {
        EnsureConnected();
        if (_commandCache.TryGetValue(query, out var cachedCmd))
        {
            if (_transaction != null) cachedCmd.Transaction = (DbTransaction)_transaction;
            UpdateParameters(cachedCmd, parameters);
            return (cachedCmd, true);
        }

        var cmd = (DbCommand)_connection.CreateCommand();
        cmd.CommandText = query;
        if (_transaction != null) cmd.Transaction = (DbTransaction)_transaction;
        AttachParameters(cmd, parameters);

        try
        {
            await cmd.PrepareAsync();
            _commandCache[query] = cmd;
            return (cmd, true);
        }
        catch
        {
            return (cmd, false);
        }
    }

    private static void AttachParameters(DbCommand command, IEnumerable<IDbDataParameter>? parameters)
    {
        if (parameters == null) return;
        foreach (var p in parameters)
        {
            command.Parameters.Add(p);
        }
    }

    private static void UpdateParameters(DbCommand command, IEnumerable<IDbDataParameter>? parameters)
    {
        if (parameters == null) return;
        int i = 0;
        foreach (var p in parameters)
        {
            if (i < command.Parameters.Count)
            {
                command.Parameters[i].Value = p.Value ?? DBNull.Value;
            }
            else
            {
                command.Parameters.Add(p);
            }
            i++;
        }
    }

    private static dynamic MapRowToDynamic(IDataReader reader)
    {
        var expando = new ExpandoObject() as IDictionary<string, object?>;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            string name = reader.GetName(i);
            object value = reader.IsDBNull(i) ? null! : reader.GetValue(i);
            expando[name] = value;
        }
        return expando;
    }

    private void LogIfEnabled(string query, long startTimestamp)
    {
        if (_serviceOptions?.EnableLogging == true)
        {
            long elapsed = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            if (_serviceOptions.LogExecutedQuery)
            {
                Console.WriteLine($"[JadeDbClient] [{_dialect}] Executed Query: {query}");
            }
            Console.WriteLine($"[JadeDbClient] [{_dialect}] Execution Time: {elapsed} ms");
        }
    }
}
