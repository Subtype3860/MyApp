using ExcelDataReader;
using ExcelDataReader.Exceptions;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MyApp.Application.Services;

public sealed partial class VehicleService
{
    public async Task<ServiceResult<Guid>> AddDefectAsync(
        Guid vehicleId,
        VehicleDefectRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(
                createdBy, ["Механик"], cancellationToken))
        {
            return ServiceResult<Guid>.Unauthorized();
        }
        var symptoms = Clean(request.Symptoms);
        var downtimeStartedAt = request.DowntimeStartedAt;
        return await CreateAsync(
            vehicleId,
            ValidateDefect(request, symptoms, downtimeStartedAt),
            () => repository.AddDefectAsync(
                vehicleId,
                request with
                {
                    ErrorCode = Clean(request.ErrorCode),
                    Symptoms = symptoms
                },
                createdBy,
                cancellationToken),
            cancellationToken);
    }

    public async Task<ServiceResult<bool>> ClaimDefectAsync(
        Guid defectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(
                userId, ExecutorProfessions, cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }
        if (!await repository.DefectExistsAsync(defectId, cancellationToken))
        {
            return ServiceResult<bool>.NotFound();
        }
        return await repository.ClaimDefectAsync(
            defectId, userId, cancellationToken)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.Conflict("Задача уже назначена.");
    }

    public async Task<ServiceResult<Guid>> CompleteDefectAsync(
        Guid defectId,
        VehicleWorkRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        if (user is null || !IsAdministrator(user) &&
            !ExecutorProfessions.Contains(user.Profession.Name.Trim()))
        {
            return ServiceResult<Guid>.Unauthorized();
        }
        request = request with { DefectId = defectId };
        var cause = Clean(request.Cause);
        var status = Clean(request.Status).ToLowerInvariant();
        request = request with
        {
            Description = Clean(request.Description),
            Cause = cause,
            Status = status,
            RequiredParts = Clean(request.RequiredParts)
        };
        var validation = ValidateWork(request);
        if (validation is not null)
        {
            return ServiceResult<Guid>.Validation(
                new Dictionary<string, string[]>
                {
                    ["vehicle"] = [validation]
                });
        }
        if (!await repository.DefectExistsAsync(defectId, cancellationToken))
        {
            return ServiceResult<Guid>.NotFound();
        }
        var id = await repository.CompleteDefectAsync(
            defectId, request, userId, IsAdministrator(user), cancellationToken);
        return id.HasValue
            ? ServiceResult<Guid>.Success(id.Value)
            : ServiceResult<Guid>.Conflict(
                "Задача не назначена вам или уже завершена.");
    }

    private static string? ValidateDefect(
        VehicleDefectRequest request,
        string symptoms,
        DateTimeOffset downtimeStartedAt) =>
        ValidateText(symptoms, "Симптомы")
        ?? (Clean(request.ErrorCode).Length > 100
            ? "Код ошибки не должен превышать 100 символов."
            : downtimeStartedAt == default
                ? "Укажите время начала простоя."
                : downtimeStartedAt > DateTimeOffset.UtcNow.AddMinutes(5)
                    ? "Время начала простоя не может быть в будущем."
                    : null);
}
