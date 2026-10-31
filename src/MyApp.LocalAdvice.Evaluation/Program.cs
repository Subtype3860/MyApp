using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MyApp.Application.Services;
using Npgsql;

var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
if (args.Length is 2 or 3 && args[0] == "--audit")
{
    // Never boot the API/initializer: this command can only read aggregate values.
    var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(args[1]))
        .AddUserSecrets("82a773f8-5e77-4ebd-8f0c-bdd7125dfa67").Build();
    var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection")) {
        Timeout = 10, CommandTimeout = 20, IncludeErrorDetail = false, ApplicationName = "AtlasLocalAdviceReadOnlyAudit"
    };
    if (args.Length == 3) connection.Host = args[2];
    try
    {
        await using var db = new NpgsqlConnection(connection.ConnectionString);
        await db.OpenAsync();
        await using var transaction = await db.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", db, transaction)) await readOnly.ExecuteNonQueryAsync();
        var queries = new Dictionary<string, string> {
            ["vehicles"] = "SELECT count(*) FROM number_car",
            ["defects"] = "SELECT count(*) FROM vehicle_defects",
            ["defectsWithNode"] = "SELECT count(*) FROM vehicle_defects WHERE btrim(node_name) <> ''",
            ["defectsWithSymptoms"] = "SELECT count(*) FROM vehicle_defects WHERE btrim(symptoms) <> ''",
            ["causeEqualsSymptoms"] = "SELECT count(*) FROM vehicle_defects WHERE btrim(symptoms) <> '' AND failure_reason = symptoms",
            ["futureDefects"] = "SELECT count(*) FROM vehicle_defects WHERE created_at > now()",
            ["repairs"] = "SELECT count(*) FROM vehicle_works WHERE defect_id IS NOT NULL AND repair_status = 'repaired'",
            ["repairsWithCompletion"] = "SELECT count(*) FROM vehicle_works WHERE defect_id IS NOT NULL AND repair_status = 'repaired' AND completed_at IS NOT NULL",
            ["hourReadings"] = "SELECT count(*) FROM vehicle_hour_readings",
            ["defectHistoryDays"] = "SELECT coalesce(extract(day FROM max(created_at)-min(created_at)),0)::bigint FROM vehicle_defects",
            ["outcomesTableExists"] = "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'vehicle_shift_outcomes'"
        };
        var counts = new Dictionary<string, long>();
        foreach (var (name, query) in queries) {
            await using var command = new NpgsqlCommand(query, db, transaction);
            counts[name] = Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        if (counts["outcomesTableExists"] == 1) {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM vehicle_shift_outcomes", db, transaction);
            counts["confirmedOutcomes"] = Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        Console.WriteLine(JsonSerializer.Serialize(new { status = "audited", generatedAt = DateTimeOffset.UtcNow, counts,
            validationStatus = "not_validated", note = "Aggregate audit only. No production text, identifiers or credentials exported." }, jsonOptions));
        await transaction.RollbackAsync();
    }
    catch (Exception error) when (error is NpgsqlException or TimeoutException) {
        Console.WriteLine(JsonSerializer.Serialize(new { status = "unavailable", errorType = error.GetType().Name,
            sqlState = (error as PostgresException)?.SqlState, innerType = error.InnerException?.GetType().Name,
            note = "Read-only PostgreSQL audit unavailable; no source records logged." }, jsonOptions));
        Environment.ExitCode = 2;
    }
}
else if (args.Length == 3 && args[0] == "--evaluate")
{
    var samples = JsonSerializer.Deserialize<ExtractionSample[]>(await File.ReadAllTextAsync(args[1]), jsonOptions) ?? [];
    var cutoff = DateTimeOffset.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
    Console.WriteLine(JsonSerializer.Serialize(LocalAdviceEvaluation.Evaluate(samples, cutoff), jsonOptions));
}
else {
    Console.WriteLine("Usage: --audit <appsettings.json> | --evaluate <local-anonymized-samples.json> <UTC-cutoff>");
    Environment.ExitCode = 1;
}
