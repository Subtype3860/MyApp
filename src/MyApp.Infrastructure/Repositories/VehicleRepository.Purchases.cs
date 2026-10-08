using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
    public Task<Guid> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken) =>
        InsertAsync(
            """
            INSERT INTO vehicle_purchase_requests (
                id, vehicle_id, request_date, request_number, item_name,
                quantity, status, note, created_by)
            VALUES (
                @id, @vehicleId, @date, @number, @item, @quantity,
                @status, @note, @createdBy)
            """,
            vehicleId,
            createdBy,
            cancellationToken,
            ("date", request.RequestDate),
            ("number", request.RequestNumber),
            ("item", request.ItemName),
            ("quantity", request.Quantity),
            ("status", request.Status),
            ("note", request.Note));

    private async Task<IReadOnlyList<VehiclePurchaseResponse>> GetPurchasesAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        await using var command = CreateRangeCommand(
            """
            SELECT id, request_date, request_number, item_name, quantity,
                   status, note, created_at
            FROM vehicle_purchase_requests
            WHERE vehicle_id = @vehicleId
              AND (@from IS NULL OR request_date >= @from)
              AND (@to IS NULL OR request_date <= @to)
            ORDER BY request_date DESC, created_at DESC
            """,
            vehicleId,
            from,
            to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<VehiclePurchaseResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new VehiclePurchaseResponse(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return result;
    }
}
