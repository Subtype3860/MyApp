using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class EmployeeSignatureRepository(
    NpgsqlDataSource dataSource) : IEmployeeSignatureRepository
{
    public async Task<EmployeeSignatureResponse?> GetAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT content, content_type
            FROM employee_signatures
            WHERE employee_id = @employeeId
            """);
        AddEmployeeIdParameter(command, employee);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new EmployeeSignatureResponse(
                reader.GetFieldValue<byte[]>(0),
                reader.GetString(1))
            : null;
    }

    public async Task SaveAsync(
        EmployeeSignatureKey employee,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO employee_signatures (
                employee_id,
                content,
                content_type)
            VALUES (
                @employeeId,
                @content,
                @contentType)
            ON CONFLICT (employee_id)
            DO UPDATE SET
                content = EXCLUDED.content,
                content_type = EXCLUDED.content_type,
                updated_at = NOW()
            """);
        AddEmployeeIdParameter(command, employee);
        command.Parameters.AddWithValue("content", content);
        command.Parameters.AddWithValue("contentType", contentType);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            DELETE FROM employee_signatures
            WHERE employee_id = @employeeId
            """);
        AddEmployeeIdParameter(command, employee);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static void AddEmployeeIdParameter(
        NpgsqlCommand command,
        EmployeeSignatureKey employee)
    {
        command.Parameters.AddWithValue("employeeId", employee.EmployeeId);
    }
}
