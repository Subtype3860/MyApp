using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;
using Npgsql;

namespace MyApp.Infrastructure.Repositories;

public sealed class EmployeeSignatureRepository(
    AppDbContext db) : IEmployeeSignatureRepository
{
    public async Task<EmployeeSignatureResponse?> GetAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken)
    {
        var signature = await db.EmployeeSignatures
            .AsNoTracking()
            .Where(signature =>
                signature.LastName == employee.LastName &&
                signature.FirstName == employee.FirstName &&
                signature.Patronymic == employee.Patronymic)
            .Select(signature => new {
                signature.Content,
                signature.ContentType
            })
            .FirstOrDefaultAsync(cancellationToken);

        return signature is null
            ? null
            : new EmployeeSignatureResponse(
                signature.Content,
                signature.ContentType);
    }

    public async Task SaveAsync(
        EmployeeSignatureKey employee,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var signature = await FindSignatureAsync(employee, cancellationToken);
        if (signature is null)
        {
            signature = new EmployeeSignatureRecord
            {
                LastName = employee.LastName,
                FirstName = employee.FirstName,
                Patronymic = employee.Patronymic
            };
            await db.EmployeeSignatures.AddAsync(signature, cancellationToken);
            signature.Content = content;
            signature.ContentType = contentType;
            signature.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception) when (
                exception.InnerException is PostgresException {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: "employee_signatures_pkey"
                })
            {
                db.Entry(signature).State = EntityState.Detached;
                var updated = await db.EmployeeSignatures
                    .Where(existing =>
                        existing.LastName == employee.LastName &&
                        existing.FirstName == employee.FirstName &&
                        existing.Patronymic == employee.Patronymic)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(existing => existing.Content, content)
                            .SetProperty(existing => existing.ContentType, contentType)
                            .SetProperty(
                                existing => existing.UpdatedAt,
                                DateTimeOffset.UtcNow),
                        cancellationToken);
                if (updated != 1)
                {
                    throw;
                }
                return;
            }
        }

        signature.Content = content;
        signature.ContentType = contentType;
        signature.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken)
    {
        var deleted = await db.EmployeeSignatures
            .Where(signature =>
                signature.LastName == employee.LastName &&
                signature.FirstName == employee.FirstName &&
                signature.Patronymic == employee.Patronymic)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return deleted > 0;
    }

    private Task<EmployeeSignatureRecord?> FindSignatureAsync(
        EmployeeSignatureKey employee,
        CancellationToken cancellationToken) =>
        db.EmployeeSignatures.FirstOrDefaultAsync(
            signature =>
                signature.LastName == employee.LastName &&
                signature.FirstName == employee.FirstName &&
                signature.Patronymic == employee.Patronymic,
            cancellationToken);
}
