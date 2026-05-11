using Microsoft.Data.Sqlite;

namespace Autotrade.Testing.Db;

/// <summary>
/// SQLite 内存库（保持连接打开即可保持数据库存活）。
/// 适合用于 EF Core 的快速集成测试（不依赖外部 Postgres）。
/// </summary>
public sealed class SqliteInMemoryDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteInMemoryDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public SqliteConnection Connection => _connection;

    public ValueTask DisposeAsync()
    {
        return _connection.DisposeAsync();
    }
}

