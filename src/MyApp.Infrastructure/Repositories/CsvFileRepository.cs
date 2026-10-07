using MyApp.Application.Abstractions;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class CsvFileRepository(NpgsqlDataSource dataSource) : ICsvFileRepository
{
    public async Task<byte[]> GetAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        var path = await ResolvePathAsync(
            connection,
            fileName,
            cancellationToken);
        await using var readCommand = connection.CreateCommand();
        readCommand.CommandText = "SELECT pg_read_binary_file(@path)";
        readCommand.Parameters.AddWithValue("path", path);
        return (byte[])(await readCommand.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"Не удалось прочитать CSV-файл «{fileName}»."));
    }

    public async Task ReplaceAsync(
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(
            cancellationToken);
        var path = await ResolvePathAsync(
            connection,
            fileName,
            cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        await using var createCommand = connection.CreateCommand();
        createCommand.Transaction = transaction;
        createCommand.CommandText = "SELECT lo_from_bytea(0, @content)";
        createCommand.Parameters.AddWithValue("content", content);
        var objectId = (uint)(await createCommand.ExecuteScalarAsync(
            cancellationToken) ?? throw new InvalidOperationException(
                "Не удалось подготовить CSV-файл к записи."));

        await using var exportCommand = connection.CreateCommand();
        exportCommand.Transaction = transaction;
        exportCommand.CommandText =
            "SELECT lo_export(CAST(@objectId AS oid), @path)";
        exportCommand.Parameters.AddWithValue("objectId", (long)objectId);
        exportCommand.Parameters.AddWithValue("path", path);
        var exportResult = Convert.ToInt32(
            await exportCommand.ExecuteScalarAsync(cancellationToken));
        if (exportResult != 1)
        {
            throw new InvalidOperationException(
                $"Не удалось записать CSV-файл «{fileName}».");
        }

        await using var unlinkCommand = connection.CreateCommand();
        unlinkCommand.Transaction = transaction;
        unlinkCommand.CommandText =
            "SELECT lo_unlink(CAST(@objectId AS oid))";
        unlinkCommand.Parameters.AddWithValue("objectId", (long)objectId);
        var unlinkResult = Convert.ToInt32(
            await unlinkCommand.ExecuteScalarAsync(cancellationToken));
        if (unlinkResult != 1)
        {
            throw new InvalidOperationException(
                "Не удалось удалить временный объект CSV-файла.");
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<string> ResolvePathAsync(
        NpgsqlConnection connection,
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var pathCommand = connection.CreateCommand();
        pathCommand.CommandText =
            """
            SELECT options.option_value
            FROM pg_foreign_table AS foreign_tables
            CROSS JOIN LATERAL pg_options_to_table(
                foreign_tables.ftoptions) AS options
            WHERE options.option_name = 'filename'
              AND REPLACE(
                    REGEXP_REPLACE(options.option_value, '^.*[/\\]', ''),
                    E'\\',
                    '') = @fileName
            LIMIT 1
            """;
        pathCommand.Parameters.AddWithValue("fileName", fileName);
        await using var reader = await pathCommand.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"В PostgreSQL не найден внешний CSV-файл «{fileName}».");
        }
        var path = reader.GetString(0);
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Для CSV-файла «{fileName}» найдено несколько путей.");
        }
        return path;
    }
}
