using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MyApp.Infrastructure.Db;

public static class CsvDatabaseMigration
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        using var stream = typeof(CsvDatabaseMigration).Assembly.GetManifestResourceStream("MyApp.Infrastructure.CsvDatabaseMigration.sql")
            ?? throw new InvalidOperationException("CSV database migration resource is missing.");
        using var reader = new StreamReader(stream);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = await reader.ReadToEndAsync(cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
