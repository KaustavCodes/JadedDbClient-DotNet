using System.Data;
using System.Diagnostics.CodeAnalysis;
using JadeDbClient.Enums;

namespace JadeDbClient.Interfaces;

public interface IDatabaseService : IDisposable
{
    DatabaseDialect Dialect { get; }

    bool PluralizeTableNames { get; }

    IDbConnection? Connection { get; set; }

    /// <summary>
    /// Open a connection to the database
    /// </summary>
    void OpenConnection();

    /// <summary>
    /// Close the connection to the database
    /// </summary>
    void CloseConnection();

    /// <summary>
    /// Executes a SQL query asynchronously and maps the result to a collection of objects of type T.
    /// </summary>
    /// <typeparam name="T">The type of objects to which the query results will be mapped. The type T should have properties that match the column names in the query result.</typeparam>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the SQL query. Default is null.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a collection of objects of type T that represent the rows returned by the query.</returns>
    /// <exception cref="System.Data.Common.DbException">Thrown when there is an error executing the query.</exception>
    /// <exception cref="InvalidOperationException">Thrown when there is an error creating an instance of type T.</exception>
    /// <exception cref="ArgumentException">Thrown when there is an error setting a property value.</exception>
    Task<IEnumerable<T>> ExecuteQueryAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a SQL query asynchronously and maps each result row to a <see langword="dynamic"/>
    /// object whose properties correspond to the column names returned by the query.
    /// Use this overload when the query spans multiple tables (e.g. via JOINs) and the
    /// result set does not correspond to a single strongly-typed model.
    /// </summary>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the SQL query. Default is null.</param>
    /// <returns>A task whose result is a collection of <see langword="dynamic"/> objects, one per row.</returns>
    Task<IEnumerable<dynamic>> ExecuteQueryDynamicAsync(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a SQL query asynchronously and returns the first result object of type T.
    /// </summary>
    /// <typeparam name="T">The type of objects to which the query results will be mapped. The type T should have properties that match the column names in the query result.</typeparam>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the SQL query. Default is null.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a collection of objects of type T that represent the rows returned by the query.</returns>
    /// <exception cref="System.Data.Common.DbException">Thrown when there is an error executing the query.</exception>
    /// <exception cref="InvalidOperationException">Thrown when there is an error creating an instance of type T.</exception>
    /// <exception cref="ArgumentException">Thrown when there is an error setting a property value.</exception>
    Task<T?> ExecuteQueryFirstRowAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a SQL query asynchronously and maps the first result row to a
    /// <see langword="dynamic"/> object whose properties correspond to the column names.
    /// Returns <c>null</c> when the query returns no rows.
    /// Use this overload when the result set spans multiple tables and does not map to a
    /// single strongly-typed model.
    /// </summary>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the SQL query. Default is null.</param>
    /// <returns>A task whose result is a <see langword="dynamic"/> object for the first row, or <c>null</c>.</returns>
    Task<dynamic?> ExecuteQueryFirstRowDynamicAsync(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a query and returns a single value (scalar) result.
    /// </summary>
    /// <param name="query">The SQL query to be executed.</param>
    /// <param name="parameters">>A collection of parameters to be used in the SQL query. Default is null.</param>
    Task<T?> ExecuteScalar<T>(string query, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a stored procedure asynchronously and returns the number of rows affected.
    /// </summary>
    /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the stored procedure. Default is null.</param>
    /// <returns>The number of rows effected after executing the stored procedure.</returns>
    /// <exception cref="SqlException">Thrown when there is an error executing the stored procedure.</exception>
    Task<int> ExecuteStoredProcedureAsync(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a stored procedure asynchronously and maps the result to a collection of objects of type T.
    /// </summary>
    /// <typeparam name="T">The type of objects to which the stored procedure results will be mapped. The type T should have properties that match the column names in the result set.</typeparam>
    /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the stored procedure. Default is null.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a collection of objects of type T that represent the rows returned by the stored procedure.</returns>
    /// <exception cref="System.Data.Common.DbException">Thrown when there is an error executing the stored procedure.</exception>
    /// <exception cref="InvalidOperationException">Thrown when there is an error creating an instance of type T.</exception>
    /// <exception cref="ArgumentException">Thrown when there is an error setting a property value.</exception>
    Task<IEnumerable<T>> ExecuteStoredProcedureSelectDataAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(string storedProcedureName, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Executes a stored procedure asynchronously and retrieves the output parameters.
    /// </summary>
    /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the stored procedure. This includes input, output, and input-output parameters.</param>
    /// <returns>A task representing the asynchronous operation. The task result contains a dictionary where the keys are the names of the output parameters and the values are their corresponding values.</returns>
    /// <exception cref="System.Data.Common.DbException">Thrown when there is an error executing the stored procedure.</exception>
    Task<Dictionary<string, object>> ExecuteStoredProcedureWithOutputAsync(string storedProcedureName, IEnumerable<IDbDataParameter> parameters);


    /// <summary>
    /// Executes a SQL command asynchronously.
    /// </summary>
    /// <param name="commandText">The SQL command to be executed.</param>
    /// <param name="parameters">A collection of parameters to be used in the SQL command. Default is null.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="System.Data.Common.DbException">Thrown when there is an error executing the command.</exception>
    Task ExecuteCommandAsync(string command, IEnumerable<IDbDataParameter>? parameters = null);

    /// <summary>
    /// Creates a new instance of an <see cref="IDbDataParameter"/> for your Database.
    /// </summary>
    /// <param name="name">The name of the parameter.</param>
    /// <param name="value">The value of the parameter.</param>
    /// <param name="dbType">The <see cref="DbType"/> of the parameter.</param>
    /// <param name="direction">The <see cref="ParameterDirection"/> of the parameter. Default is <see cref="ParameterDirection.Input"/>.</param>
    /// <param name="size">The size of the parameter. Default is 0.</param>
    /// <returns>A new instance of <see cref="SqlParameter"/> configured with the specified properties.</returns>
    IDbDataParameter GetParameter(string name, object value, DbType dbType, ParameterDirection direction = ParameterDirection.Input, int size = 0);

    /// <summary>
    /// Bulk inserts a DataTable into a Database table.
    /// </summary>
    /// <param name="dataTable">The DataTable to insert.</param>
    /// <param name="tableName">The target PostgreSQL table name.</param>
    Task<bool> InsertDataTable(string tableName, DataTable dataTable);

    /// <summary>
    /// Bulk inserts a DataTable into a Database table with JsonDatas.
    /// </summary>
    /// <param name="dataTable">The DataTable to insert.</param>
    /// <param name="tableName">The target PostgreSQL table name.</param>
    Task<bool> InsertDataTableWithJsonData(string tableName, DataTable dataTable);

    /// <summary>
    /// Bulk inserts a collection of objects into a Database table using streaming for memory efficiency.
    /// </summary>
    /// <typeparam name="T">The type of objects to insert. Properties should match database column names.</typeparam>
    /// <param name="tableName">The target database table name.</param>
    /// <param name="items">The collection of items to insert.</param>
    /// <param name="batchSize">Number of records to insert per batch (default 1000).</param>
    /// <returns>The total number of rows inserted.</returns>
    Task<int> BulkInsertAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string tableName, IEnumerable<T> items, int batchSize = 1000);

    /// <summary>
    /// Bulk inserts a stream of objects into a Database table with progress reporting.
    /// </summary>
    /// <typeparam name="T">The type of objects to insert. Properties should match database column names.</typeparam>
    /// <param name="tableName">The target database table name.</param>
    /// <param name="items">The async enumerable stream of items to insert.</param>
    /// <param name="progress">Optional progress reporter that receives the count of rows inserted.</param>
    /// <param name="batchSize">Number of records to insert per batch (default 1000).</param>
    /// <returns>The total number of rows inserted.</returns>
    Task<int> BulkInsertAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(string tableName, IAsyncEnumerable<T> items, IProgress<int>? progress = null, int batchSize = 1000);

    /// <summary>
    /// Begins a database transaction.
    /// </summary>
    /// <returns>An IDbTransaction object representing the new transaction.</returns>
    IDbTransaction BeginTransaction();

    /// <summary>
    /// Begins a database transaction with the specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction.</param>
    /// <returns>An IDbTransaction object representing the new transaction.</returns>
    IDbTransaction BeginTransaction(IsolationLevel isolationLevel);

    /// <summary>
    /// Commits the current database transaction.
    /// </summary>
    /// <param name="transaction">The transaction to commit.</param>
    void CommitTransaction(IDbTransaction transaction);

    /// <summary>
    /// Rolls back the current database transaction.
    /// </summary>
    /// <param name="transaction">The transaction to roll back.</param>
    void RollbackTransaction(IDbTransaction transaction);

    /// <summary>
    /// Opens an active, single-connection database session for executing multiple queries efficiently.
    /// Dispose or async dispose the returned <see cref="IDatabaseSession"/> when finished to return the connection to the pool.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An open <see cref="IDatabaseSession"/>.</returns>
    Task<IDatabaseSession> OpenSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously opens an active, single-connection database session for executing multiple queries efficiently.
    /// </summary>
    /// <returns>An open <see cref="IDatabaseSession"/>.</returns>
    IDatabaseSession OpenSession();
}