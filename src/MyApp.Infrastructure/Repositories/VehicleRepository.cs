using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository(NpgsqlDataSource dataSource, IVehiclePartsRepository partsRepository, IVehiclePurchaseRepository purchaseRepository, IVehicleHoursRepository hoursRepository) : IVehicleRepository
{
    private const string ImageDirectory = "/mnt/dietpi/img";
    private const string VideoDirectory = "/mnt/dietpi/video";
    private const string VehicleSelect =
        """
        SELECT
            vehicles.id,
            COALESCE(groups.full_name, ''),
            COALESCE(types.full_name, ''),
            COALESCE(models.full_name, ''),
            vehicles.gar_number,
            COALESCE(vehicles.gos_number, ''),
            COALESCE(vehicles.vim, '')
        FROM number_car AS vehicles
        LEFT JOIN model_car AS models ON models.id = vehicles.car_mode_id
        LEFT JOIN type_car AS types ON types.id = models.car_type_id
        LEFT JOIN group_car AS groups ON groups.id = types.car_group_id
        """;

    public async Task<IReadOnlyList<VehicleResponse>> GetVehiclesAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            VehicleSelect +
            """

            ORDER BY
                COALESCE(groups.full_name, ''),
                COALESCE(types.full_name, ''),
                COALESCE(models.full_name, ''),
                vehicles.gar_number NULLS LAST
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<VehicleResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadVehicle(reader));
        }
        return result;
    }

    public async Task<VehicleJournalResponse?> GetJournalAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var vehicle = await GetVehicleAsync(vehicleId, cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        var purchases = await GetPurchasesAsync(
            vehicleId, from, to, cancellationToken);
        var defects = await GetDefectsAsync(
            vehicleId, from, to, cancellationToken);
        var hours = await GetHoursAsync(
            vehicleId, from, to, cancellationToken);
        var works = await GetWorksAsync(
            vehicleId, from, to, cancellationToken);
        return new VehicleJournalResponse(
            vehicle, purchases, defects, hours, works);
    }

    public async Task<bool> VehicleExistsAsync(
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM number_car WHERE id = @vehicleId)");
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> DeleteEntryAsync(
        string category,
        Guid id,
        CancellationToken cancellationToken)
    {
        var table = category switch
        {
            "purchases" => "vehicle_purchase_requests",
            "defects" => "vehicle_defects",
            "hours" => "vehicle_hour_readings",
            "works" => "vehicle_works",
            _ => null
        };
        if (table is null)
        {
            return false;
        }

        var paths = category switch
        {
            "defects" => await GetDefectMediaPathsAsync(id, cancellationToken),
            "works" => await GetWorkMediaPathsAsync(id, cancellationToken),
            _ => []
        };
        if (paths.Any(path => !IsManagedPath(path)))
        {
            throw new InvalidOperationException(
                "A repair media file is outside the managed storage directories.");
        }
        await using var command = dataSource.CreateCommand(
            $"DELETE FROM {table} WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted)
        {
            foreach (var path in paths)
            {
                DeleteManagedFile(path);
            }
        }
        return deleted;
    }

    private async Task<VehicleResponse?> GetVehicleAsync(
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            VehicleSelect + "\nWHERE vehicles.id = @vehicleId");
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadVehicle(reader)
            : null;
    }

    private NpgsqlCommand CreateRangeCommand(
        string sql,
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to)
    {
        var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        command.Parameters.AddWithValue(
            "from",
            NpgsqlTypes.NpgsqlDbType.Date,
            from is null ? DBNull.Value : from.Value);
        command.Parameters.AddWithValue(
            "to",
            NpgsqlTypes.NpgsqlDbType.Date,
            to is null ? DBNull.Value : to.Value);
        return command;
    }

    private async Task<Guid> InsertAsync(
        string sql,
        Guid vehicleId,
        Guid createdBy,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var id = Guid.NewGuid();
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("vehicleId", vehicleId);
        command.Parameters.AddWithValue("createdBy", createdBy);
        foreach (var (name, value) in parameters)
        {
            if (value is null)
            {
                command.Parameters.Add(
                    name,
                    name == "resolvedDate"
                        ? NpgsqlTypes.NpgsqlDbType.Date
                        : NpgsqlTypes.NpgsqlDbType.Numeric).Value = DBNull.Value;
            }
            else
            {
                command.Parameters.AddWithValue(name, value);
            }
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private static VehicleResponse ReadVehicle(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6));
}
