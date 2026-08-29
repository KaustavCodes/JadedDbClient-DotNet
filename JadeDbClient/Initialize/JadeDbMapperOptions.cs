using System;
using System.Collections.Generic;
using System.Data;

namespace JadeDbClient.Initialize;

public class JadeDbMapperOptions
{
    // Static bridge: Source Generator drops mappers here at startup
    internal static readonly Dictionary<Type, Func<IDataReader, object>> GlobalMappers = new();
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Delegate> GlobalTypedMappers = new();

    // Property accessors for bulk insert operations (reflection-free)
    internal static readonly Dictionary<Type, BulkInsertAccessor> GlobalBulkInsertAccessors = new();

    internal readonly Dictionary<Type, Func<IDataReader, object>> Mappers = new();
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Delegate> _typedMappers = new();

    public JadeDbMapperOptions()
    {
        // Pull globally generated mappers into this instance automatically
        foreach (var mapper in GlobalMappers)
        {
            Mappers[mapper.Key] = mapper.Value;
        }
        foreach (var kvp in GlobalTypedMappers)
        {
            _typedMappers[kvp.Key] = kvp.Value;
        }
    }

    public void RegisterMapper<T>(Func<IDataReader, T> mapper) where T : class
    {
        Mappers[typeof(T)] = (reader) => mapper(reader);
        _typedMappers[typeof(T)] = mapper;
    }

    // Public method for testing - checks if mapper exists
    public bool HasMapper<T>()
    {
        return Mappers.ContainsKey(typeof(T));
    }

    // Public method for testing - executes mapper if it exists
    public T? ExecuteMapper<T>(IDataReader reader) where T : class
    {
        if (TryGetMapper<T>(out var mapper) && mapper != null)
        {
            return mapper(reader);
        }
        return null;
    }

    // Public method for testing - allows registering in GlobalMappers (simulating Source Generator)
    public static void RegisterGlobalMapper<T>(Func<IDataReader, T> mapper) where T : class
    {
        GlobalMappers[typeof(T)] = (reader) => mapper(reader);
        GlobalTypedMappers[typeof(T)] = mapper;
    }

    internal bool TryGetMapper<T>(out Func<IDataReader, T>? mapper)
    {
        if (_typedMappers.TryGetValue(typeof(T), out var del) && del is Func<IDataReader, T> typed)
        {
            mapper = typed;
            return true;
        }
        if (GlobalTypedMappers.TryGetValue(typeof(T), out var gDel) && gDel is Func<IDataReader, T> gTyped)
        {
            _typedMappers[typeof(T)] = gTyped;
            mapper = gTyped;
            return true;
        }
        if (Mappers.TryGetValue(typeof(T), out var func))
        {
            Func<IDataReader, T> created = (reader) => (T)func(reader);
            _typedMappers[typeof(T)] = created;
            mapper = created;
            return true;
        }
        mapper = null;
        return false;
    }

    // Public method for registering bulk insert accessors (used by source generator)
    public static void RegisterBulkInsertAccessor<T>(string[] columnNames, Func<T, object?[]> accessor)
    {
        GlobalBulkInsertAccessors[typeof(T)] = new BulkInsertAccessor(columnNames, obj => accessor((T)obj));
    }

    // Internal method to try getting bulk insert accessor
    public static bool TryGetBulkInsertAccessor<T>(out BulkInsertAccessor? accessor)
    {
        return GlobalBulkInsertAccessors.TryGetValue(typeof(T), out accessor);
    }
}

/// <summary>
/// Holds reflection-free property accessor information for bulk insert operations
/// </summary>
public class BulkInsertAccessor
{
    public string[] ColumnNames { get; }
    public Func<object, object?[]> GetValues { get; }

    public BulkInsertAccessor(string[] columnNames, Func<object, object?[]> getValues)
    {
        ColumnNames = columnNames;
        GetValues = getValues;
    }
}