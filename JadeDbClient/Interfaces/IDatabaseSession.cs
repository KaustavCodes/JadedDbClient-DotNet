using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using JadeDbClient.Enums;

namespace JadeDbClient.Interfaces;

/// <summary>
/// Represents an active, open database session that executes queries over a single persistent connection.
/// Implements <see cref="IAsyncDisposable"/> and <see cref="IDisposable"/> for clean connection cleanup.
/// </summary>
public interface IDatabaseSession : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the underlying active database connection.
    /// </summary>
    IDbConnection Connection { get; }

    /// <summary>
    /// Gets the current active transaction, or null if no transaction is active.
    /// </summary>
    IDbTransaction? Transaction { get; }

    /// <summary>
    /// Gets the SQL dialect for this session.
    /// </summary>
    DatabaseDialect Dialect { get; }

    /// <summary>
    /// Gets whether table names should be pluralized by default.
    /// </summary>
    bool PluralizeTableNames { get; }

    /// <summary>
    /// Begins a database transaction on this session.
    /// </summary>
    IDbTransaction BeginTransaction();

    /// <summary>
    /// Begins a database transaction with the specified isolation level on this session.
    /// </summary>
    IDbTransaction BeginTransaction(IsolationLevel isolationLevel);

    /// <summary>
    /// Commits the active transaction on this session.
    /// </summary>
    void CommitTransaction();

    /// <summary>
    /// Rolls back the active transaction on this session.
    /// </summary>
    void RollbackTransaction();

    /// <summary>
    /// Executes a SQL query asynchronously and maps the result to a collection of objects of type T.
    /// </summary>
    Task<IEnumerable<T>> ExecuteQueryAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a SQL query asynchronously and maps each result row to a dynamic object.
    /// </summary>
    Task<IEnumerable<dynamic>> ExecuteQueryDynamicAsync(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first result object of type T, or default.
    /// </summary>
    Task<T?> ExecuteQueryFirstRowAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a SQL query asynchronously and maps the first result row to a dynamic object.
    /// </summary>
    Task<dynamic?> ExecuteQueryFirstRowDynamicAsync(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a query and returns a single scalar value.
    /// </summary>
    Task<T?> ExecuteScalar<T>(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a SQL command asynchronously and returns the number of rows affected.
    /// </summary>
    Task<int> ExecuteCommandAsync(string command, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a stored procedure asynchronously and returns the number of rows affected.
    /// </summary>
    Task<int> ExecuteStoredProcedureAsync(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a stored procedure asynchronously and maps the result to a collection of objects of type T.
    /// </summary>
    Task<IEnumerable<T>> ExecuteStoredProcedureSelectDataAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a stored procedure asynchronously and retrieves the output parameters.
    /// </summary>
    Task<Dictionary<string, object>> ExecuteStoredProcedureWithOutputAsync(string storedProcedureName, IEnumerable<IDbDataParameter> parameters);

    /// <summary>
    /// Creates a new database parameter.
    /// </summary>
    IDbDataParameter GetParameter(string name, object value, DbType dbType, ParameterDirection direction = ParameterDirection.Input, int size = 0);
}
