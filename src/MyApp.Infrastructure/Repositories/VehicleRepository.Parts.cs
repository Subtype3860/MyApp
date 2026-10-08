using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class VehicleRepository
{
    public async Task<bool> UpdatePartsRequestAsync(
        Guid workId,
        VehiclePartsRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE vehicle_works
            SET purchase_request_number = @number,
                purchase_request_date = @date,
                purchase_request_file_name = @fileName,
                purchase_request_content_type = @contentType,
                purchase_request_content = @content
            WHERE id = @workId AND repair_status = 'awaiting_parts'
            """);
        command.Parameters.AddWithValue("workId", workId);
        command.Parameters.AddWithValue("number", request.RequestNumber);
        command.Parameters.AddWithValue("date", request.RequestDate);
        AddNullable(command, "fileName", NpgsqlTypes.NpgsqlDbType.Varchar, request.FileName);
        AddNullable(command, "contentType", NpgsqlTypes.NpgsqlDbType.Varchar, request.ContentType);
        AddNullable(command, "content", NpgsqlTypes.NpgsqlDbType.Bytea, request.Content);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<VehicleRequestFileContent?> GetPartsRequestFileAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT purchase_request_file_name,
                   purchase_request_content_type,
                   purchase_request_content
            FROM vehicle_works
            WHERE id = @workId AND purchase_request_content IS NOT NULL
            """);
        command.Parameters.AddWithValue("workId", workId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new VehicleRequestFileContent(
                reader.GetString(0), reader.GetString(1),
                reader.GetFieldValue<byte[]>(2))
            : null;
    }

    public async Task<bool> DeletePartsRequestAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE vehicle_works
            SET purchase_request_number = NULL,
                purchase_request_date = NULL,
                purchase_request_file_name = NULL,
                purchase_request_content_type = NULL,
                purchase_request_content = NULL
            WHERE id = @workId
            """);
        command.Parameters.AddWithValue("workId", workId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
