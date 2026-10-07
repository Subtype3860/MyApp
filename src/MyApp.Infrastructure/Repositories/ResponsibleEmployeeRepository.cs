using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class ResponsibleEmployeeRepository(
    NpgsqlDataSource dataSource) : IResponsibleEmployeeRepository
{
    public async Task<IReadOnlyList<ResponsibleEmployeeResponse>> GetByProfessionAsync(
        string profession,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT "FullName", "Profession"
            FROM v_employee
            WHERE LOWER(BTRIM("Profession")) = LOWER(@profession)
            ORDER BY "FullName"
            """);
        command.Parameters.AddWithValue("profession", profession);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var employees = new List<ResponsibleEmployeeResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = ParseFullName(reader.GetString(0));
            employees.Add(new ResponsibleEmployeeResponse(
                name.FirstName,
                name.Patronymic,
                name.LastName,
                reader.GetString(1)));
        }

        return employees;
    }

    public async Task<bool> ExistsAsync(
        ResponsibleEmployeeSelection employee,
        string profession,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM v_employee
                WHERE LOWER(BTRIM("Profession")) = LOWER(@profession)
                  AND REGEXP_REPLACE(
                        BTRIM("FullName"),
                        '\s+',
                        ' ',
                        'g') = @fullName
            )
            """);
        command.Parameters.AddWithValue("profession", profession);
        command.Parameters.AddWithValue(
            "fullName",
            string.Join(
                ' ',
                new[]
                {
                    employee.LastName,
                    employee.FirstName,
                    employee.Patronymic
                }.Where(value => !string.IsNullOrWhiteSpace(value))
                 .Select(value => value.Trim())));
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static EmployeeName ParseFullName(string fullName)
    {
        var parts = fullName.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException(
                $"The employee FullName value '{fullName}' is invalid.");
        }

        return new EmployeeName(
            parts[1],
            parts.Length > 2 ? string.Join(' ', parts.Skip(2)) : string.Empty,
            parts[0]);
    }

    private sealed record EmployeeName(
        string FirstName,
        string Patronymic,
        string LastName);
}
