#nullable enable
using Npgsql;

namespace VKR.Services;

public static class Db
{
    private static NpgsqlDataSource? _dataSource;

    public static void Initialize(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));

        _dataSource?.Dispose();
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public static NpgsqlConnection CreateConnection() =>
        _dataSource?.CreateConnection()
        ?? throw new InvalidOperationException("Database is not initialized.");
}
