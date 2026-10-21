// Headless System.Data.OleDb shape stub. Desktop apps (typed-DataSet
// TableAdapters in the Telerik demos) carry OleDbDataAdapter-typed fields,
// so the types must load; the data verbs then degrade to empty results
// instead of throwing, because no Jet/ACE provider exists in the browser.
// Same neutralization policy as the fork's P/Invoke stubs.

using System.Collections;
using System.Data.Common;

namespace System.Data.OleDb;

public enum OleDbType
{
    Empty = 0,
    SmallInt = 2,
    Integer = 3,
    Single = 4,
    Double = 5,
    Currency = 6,
    Date = 7,
    BSTR = 8,
    IDispatch = 9,
    Error = 10,
    Boolean = 11,
    Variant = 12,
    IUnknown = 13,
    Decimal = 14,
    TinyInt = 16,
    UnsignedTinyInt = 17,
    UnsignedSmallInt = 18,
    UnsignedInt = 19,
    BigInt = 20,
    UnsignedBigInt = 21,
    Filetime = 64,
    Guid = 72,
    Binary = 128,
    Char = 129,
    WChar = 130,
    Numeric = 131,
    DBDate = 133,
    DBTime = 134,
    DBTimeStamp = 135,
    PropVariant = 138,
    VarNumeric = 139,
    VarChar = 200,
    LongVarChar = 201,
    VarWChar = 202,
    LongVarWChar = 203,
    VarBinary = 204,
    LongVarBinary = 205,
}

public sealed class OleDbError
{
    internal OleDbError(string message) => Message = message;
    public string Message { get; }
    public string? Source => null;
    public int NativeError => 0;
    public string SQLState => string.Empty;
    public override string ToString() => Message;
}

public sealed class OleDbErrorCollection : CollectionBase
{
    internal void Add(OleDbError error) => List.Add(error);
    public OleDbError this[int index] => (OleDbError)List[index]!;
}

public class OleDbException : DbException
{
    public OleDbException()
    {
    }

    public OleDbException(string message)
        : base(message)
    {
    }

    public OleDbException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public OleDbErrorCollection Errors { get; } = new();
}

public sealed class OleDbInfoMessageEventArgs : EventArgs
{
    public OleDbErrorCollection Errors { get; } = new();
    public string? Source => null;
    public string Message => string.Empty;
}

public delegate void OleDbInfoMessageEventHandler(object sender, OleDbInfoMessageEventArgs e);

public sealed class OleDbRowUpdatedEventArgs : RowUpdatedEventArgs
{
    public OleDbRowUpdatedEventArgs(DataRow row, IDbCommand? command, StatementType statementType, DataTableMapping tableMapping)
        : base(row, command, statementType, tableMapping)
    {
    }

    public new OleDbCommand? Command => (OleDbCommand?)base.Command;
}

public sealed class OleDbRowUpdatingEventArgs : RowUpdatingEventArgs
{
    public OleDbRowUpdatingEventArgs(DataRow row, IDbCommand? command, StatementType statementType, DataTableMapping tableMapping)
        : base(row, command, statementType, tableMapping)
    {
    }

    public new OleDbCommand? Command
    {
        get => (OleDbCommand?)base.Command;
        set => base.Command = value;
    }

    protected override IDbCommand? BaseCommand
    {
        get => base.BaseCommand;
        set => base.BaseCommand = value;
    }
}

public delegate void OleDbRowUpdatedEventHandler(object sender, OleDbRowUpdatedEventArgs e);

public delegate void OleDbRowUpdatingEventHandler(object sender, OleDbRowUpdatingEventArgs e);

public static class OleDbSchemaGuid
{
    public static readonly Guid Tables = new("C736EBC0-C68D-11D0-8DF9-00AA00B58E10");
    public static readonly Guid Columns = new("C736EBC3-C68D-11D0-8DF9-00AA00B58E10");
    public static readonly Guid Indexes = new("C736EBC6-C68D-11D0-8DF9-00AA00B58E10");
    public static readonly Guid Procedures = new("C736EBC9-C68D-11D0-8DF9-00AA00B58E10");
    public static readonly Guid Procedure_Parameters = new("C736EBCA-C68D-11D0-8DF9-00AA00B58E10");
    public static readonly Guid Provider_Types = new("C736EBCB-C68D-11D0-8DF9-00AA00B58E10");
    public static readonly Guid Primary_Keys = new("C736EBC7-C68D-11D0-8DF9-00AA00B58E10");
    public static readonly Guid Foreign_Keys = new("C736EBC8-C68D-11D0-8DF9-00AA00B58E10");
}

public sealed class OleDbConnection : DbConnection
{
    private ConnectionState _state = ConnectionState.Closed;
    private string _connectionString = string.Empty;

    public OleDbConnection()
    {
    }

    public OleDbConnection(string? connectionString) => _connectionString = connectionString ?? string.Empty;

    public event OleDbInfoMessageEventHandler? InfoMessage;

    public string Provider => string.Empty;

    public override string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = value ?? string.Empty;
    }

    public override string Database => string.Empty;
    public override string DataSource => string.Empty;
    public override string ServerVersion => string.Empty;
    public override ConnectionState State => _state;

    public override void Open() => _state = ConnectionState.Open;

    public override void Close() => _state = ConnectionState.Closed;

    public override void ChangeDatabase(string databaseName)
    {
    }

    public new OleDbTransaction BeginTransaction() => new(this, IsolationLevel.Unspecified);

    public new OleDbTransaction BeginTransaction(IsolationLevel isolationLevel) => new(this, isolationLevel);

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new OleDbTransaction(this, isolationLevel);

    public new OleDbCommand CreateCommand() => new() { Connection = this };

    protected override DbCommand CreateDbCommand() => CreateCommand();

    public DataTable GetOleDbSchemaTable(Guid schema, object?[]? restrictions) => new();

    public static void ReleaseObjectPool()
    {
    }

    internal void RaiseInfoMessage() => InfoMessage?.Invoke(this, new OleDbInfoMessageEventArgs());
}

public sealed class OleDbTransaction : DbTransaction
{
    internal OleDbTransaction(OleDbConnection connection, IsolationLevel isolationLevel)
    {
        Connection = connection;
        IsolationLevel = isolationLevel;
    }

    public new OleDbConnection Connection { get; }

    protected override DbConnection DbConnection => Connection;

    public override IsolationLevel IsolationLevel { get; }

    public override void Commit()
    {
    }

    public override void Rollback()
    {
    }
}

public sealed class OleDbDataReader : DbDataReader
{
    public override int Depth => 0;
    public override int FieldCount => 0;
    public override bool HasRows => false;
    public override bool IsClosed => false;
    public override int RecordsAffected => 0;

    public override object this[int i] => DBNull.Value;
    public override object this[string name] => DBNull.Value;

    public override bool Read() => false;
    public override bool NextResult() => false;
    public override void Close()
    {
    }

    public override string GetName(int i) => string.Empty;
    public override int GetOrdinal(string name) => -1;
    public override string GetDataTypeName(int i) => string.Empty;
    public override Type GetFieldType(int i) => typeof(object);
    public override object GetValue(int i) => DBNull.Value;

    public override int GetValues(object[] values) => 0;
    public override bool IsDBNull(int i) => true;

    public override bool GetBoolean(int i) => false;
    public override byte GetByte(int i) => 0;
    public override long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => 0;
    public override char GetChar(int i) => '\0';
    public override long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => 0;
    public override DateTime GetDateTime(int i) => default;
    public override decimal GetDecimal(int i) => 0m;
    public override double GetDouble(int i) => 0d;
    public override float GetFloat(int i) => 0f;
    public override Guid GetGuid(int i) => Guid.Empty;
    public override short GetInt16(int i) => 0;
    public override int GetInt32(int i) => 0;
    public override long GetInt64(int i) => 0L;
    public override string GetString(int i) => string.Empty;

    public override DataTable? GetSchemaTable() => new();

    public override IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
}

public sealed class OleDbParameter : DbParameter
{
    private string _parameterName = string.Empty;
    private string _sourceColumn = string.Empty;

    public OleDbParameter()
    {
    }

    public OleDbParameter(string? parameterName, object? value)
    {
        _parameterName = parameterName ?? string.Empty;
        Value = value;
    }

    public OleDbParameter(string? parameterName, OleDbType oleDbType)
    {
        _parameterName = parameterName ?? string.Empty;
        OleDbType = oleDbType;
    }

    public OleDbParameter(string? parameterName, OleDbType oleDbType, int size)
        : this(parameterName, oleDbType) => Size = size;

    public OleDbParameter(string? parameterName, OleDbType oleDbType, int size, string? sourceColumn)
        : this(parameterName, oleDbType, size) => _sourceColumn = sourceColumn ?? string.Empty;

    public OleDbParameter(string? parameterName, OleDbType oleDbType, int size, ParameterDirection direction,
        byte precision, byte scale, string? sourceColumn, DataRowVersion sourceVersion, bool sourceColumnNullMapping, object? value)
        : this(parameterName, oleDbType, size, sourceColumn)
    {
        Direction = direction;
        Precision = precision;
        Scale = scale;
        SourceVersion = sourceVersion;
        SourceColumnNullMapping = sourceColumnNullMapping;
        Value = value;
    }

    public OleDbType OleDbType { get; set; } = OleDbType.VarWChar;

    public override DbType DbType { get; set; } = DbType.String;

    public byte Precision { get; set; }

    public byte Scale { get; set; }

    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    public override bool IsNullable { get; set; }

    public override string ParameterName
    {
        get => _parameterName;
        set => _parameterName = value ?? string.Empty;
    }

    public override int Size { get; set; }

    public override string SourceColumn
    {
        get => _sourceColumn;
        set => _sourceColumn = value ?? string.Empty;
    }

    public override bool SourceColumnNullMapping { get; set; }

    public override DataRowVersion SourceVersion { get; set; } = DataRowVersion.Current;

    public override object? Value { get; set; }

    public override void ResetDbType()
    {
        DbType = DbType.String;
        OleDbType = OleDbType.VarWChar;
    }
}

public sealed class OleDbParameterCollection : DbParameterCollection
{
    private readonly List<OleDbParameter> _parameters = new();

    public new OleDbParameter this[int index] => _parameters[index];

    public new OleDbParameter this[string parameterName] => _parameters[IndexOf(parameterName)];

    public override int Count => _parameters.Count;

    public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

    public OleDbParameter Add(OleDbParameter value)
    {
        _parameters.Add(value);
        return value;
    }

    public OleDbParameter AddWithValue(string? parameterName, object? value) => Add(new OleDbParameter(parameterName, value));

    public override int Add(object value)
    {
        _parameters.Add((OleDbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (object value in values)
            _parameters.Add((OleDbParameter)value);
    }

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => value is OleDbParameter p && _parameters.Contains(p);

    public override bool Contains(string value) => IndexOf(value) >= 0;

    public override void CopyTo(Array array, int index) => ((ICollection)_parameters).CopyTo(array, index);

    public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    public override int IndexOf(object value) => value is OleDbParameter p ? _parameters.IndexOf(p) : -1;

    public override int IndexOf(string parameterName)
    {
        for (int i = 0; i < _parameters.Count; i++)
        {
            if (string.Equals(_parameters[i].ParameterName, parameterName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    public override void Insert(int index, object value) => _parameters.Insert(index, (OleDbParameter)value);

    public override void Remove(object value)
    {
        if (value is OleDbParameter p)
            _parameters.Remove(p);
    }

    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));

    protected override DbParameter GetParameter(int index) => _parameters[index];

    protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = (OleDbParameter)value;

    protected override void SetParameter(string parameterName, DbParameter value) => _parameters[IndexOf(parameterName)] = (OleDbParameter)value;
}

public sealed class OleDbCommand : DbCommand
{
    private readonly OleDbParameterCollection _parameters = new();
    private string _commandText = string.Empty;

    public OleDbCommand()
    {
    }

    public OleDbCommand(string? commandText) => _commandText = commandText ?? string.Empty;

    public OleDbCommand(string? commandText, OleDbConnection? connection)
        : this(commandText) => Connection = connection;

    public OleDbCommand(string? commandText, OleDbConnection? connection, OleDbTransaction? transaction)
        : this(commandText, connection) => Transaction = transaction;

    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }

    public override int CommandTimeout { get; set; } = 30;

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public new OleDbConnection? Connection { get; set; }

    protected override DbConnection? DbConnection
    {
        get => Connection;
        set => Connection = (OleDbConnection?)value;
    }

    public new OleDbTransaction? Transaction { get; set; }

    protected override DbTransaction? DbTransaction
    {
        get => Transaction;
        set => Transaction = (OleDbTransaction?)value;
    }

    public new OleDbParameterCollection Parameters => _parameters;

    protected override DbParameterCollection DbParameterCollection => _parameters;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    public override void Cancel()
    {
    }

    public override void Prepare()
    {
    }

    public new OleDbParameter CreateParameter() => new();

    protected override DbParameter CreateDbParameter() => CreateParameter();

    public new OleDbDataReader ExecuteReader() => new();

    public new OleDbDataReader ExecuteReader(CommandBehavior behavior) => new();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => new OleDbDataReader();

    public override int ExecuteNonQuery() => 0;

    public override object? ExecuteScalar() => null;
}

public sealed class OleDbDataAdapter : DbDataAdapter
{
    public OleDbDataAdapter()
    {
    }

    public OleDbDataAdapter(OleDbCommand selectCommand) => SelectCommand = selectCommand;

    public OleDbDataAdapter(string? selectCommandText, OleDbConnection? selectConnection)
        => SelectCommand = new OleDbCommand(selectCommandText, selectConnection);

    public OleDbDataAdapter(string? selectCommandText, string? selectConnectionString)
        => SelectCommand = new OleDbCommand(selectCommandText, new OleDbConnection(selectConnectionString));

    public event OleDbRowUpdatedEventHandler? RowUpdated;

    public event OleDbRowUpdatingEventHandler? RowUpdating;

    public new OleDbCommand? SelectCommand
    {
        get => (OleDbCommand?)base.SelectCommand;
        set => base.SelectCommand = value;
    }

    public new OleDbCommand? InsertCommand
    {
        get => (OleDbCommand?)base.InsertCommand;
        set => base.InsertCommand = value;
    }

    public new OleDbCommand? UpdateCommand
    {
        get => (OleDbCommand?)base.UpdateCommand;
        set => base.UpdateCommand = value;
    }

    public new OleDbCommand? DeleteCommand
    {
        get => (OleDbCommand?)base.DeleteCommand;
        set => base.DeleteCommand = value;
    }

    protected override RowUpdatedEventArgs CreateRowUpdatedEvent(DataRow row, IDbCommand? command, StatementType statementType, DataTableMapping tableMapping)
        => new OleDbRowUpdatedEventArgs(row, command, statementType, tableMapping);

    protected override RowUpdatingEventArgs CreateRowUpdatingEvent(DataRow row, IDbCommand? command, StatementType statementType, DataTableMapping tableMapping)
        => new OleDbRowUpdatingEventArgs(row, command, statementType, tableMapping);

    protected override void OnRowUpdated(RowUpdatedEventArgs value)
    {
        RowUpdated?.Invoke(this, (OleDbRowUpdatedEventArgs)value);
        base.OnRowUpdated(value);
    }

    protected override void OnRowUpdating(RowUpdatingEventArgs value)
    {
        RowUpdating?.Invoke(this, (OleDbRowUpdatingEventArgs)value);
        base.OnRowUpdating(value);
    }
}
