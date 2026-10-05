using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;

namespace MyApp.Infrastructure.Repositories;

public sealed partial class ResponsibleEmployeeRepository(
    AppDbContext db) : IResponsibleEmployeeRepository
{
    public async Task<IReadOnlyList<ResponsibleEmployeeResponse>> GetByProfessionAsync(
        string profession,
        CancellationToken cancellationToken)
    {
        var employees = await db.Employees
            .AsNoTracking()
            .Where(employee =>
                employee.Profession.Trim().ToLower() == profession.Trim().ToLower())
            .OrderBy(employee => employee.FullName)
            .Select(employee => new {
                employee.FullName,
                employee.Profession
            })
            .ToArrayAsync(cancellationToken);

        return employees
            .Select(employee => {
                var name = ParseFullName(employee.FullName);
                return new ResponsibleEmployeeResponse(
                    name.FirstName,
                    name.Patronymic,
                    name.LastName,
                    employee.Profession);
            })
            .ToArray();
    }

    public async Task<bool> ExistsAsync(
        ResponsibleEmployeeSelection employee,
        string profession,
        CancellationToken cancellationToken)
    {
        var fullName = string.Join(
            ' ',
            new[]
            {
                employee.LastName,
                employee.FirstName,
                employee.Patronymic
            }.Where(value => !string.IsNullOrWhiteSpace(value))
             .Select(value => value.Trim()));
        var candidates = await db.Employees
            .AsNoTracking()
            .Where(candidate =>
                candidate.Profession.Trim().ToLower() == profession.Trim().ToLower())
            .Select(candidate => candidate.FullName)
            .ToArrayAsync(cancellationToken);

        return candidates.Any(candidate =>
            NormalizeWhitespace(candidate) == fullName);
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

    private static string NormalizeWhitespace(string value) =>
        WhitespaceRegex().Replace(value.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private sealed record EmployeeName(
        string FirstName,
        string Patronymic,
        string LastName);
}
