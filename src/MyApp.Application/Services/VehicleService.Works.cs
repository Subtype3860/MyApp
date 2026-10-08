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
    public async Task<ServiceResult<Guid>> AddWorkAsync(
        Guid vehicleId,
        VehicleWorkRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        if (!await repository.DefectBelongsToVehicleAsync(
                request.DefectId, vehicleId, cancellationToken))
        {
            return ServiceResult<Guid>.NotFound();
        }
        return await CompleteDefectAsync(
            request.DefectId, request, createdBy, cancellationToken);
    }

    private static string? ValidateWork(VehicleWorkRequest request)
    {
        if (request.DefectId == Guid.Empty)
        {
            return "Выберите неисправность.";
        }
        var description = ValidateText(request.Description, "Выполненные работы");
        var failure = ValidateText(request.Cause, "Причина отказа");
        var status = Clean(request.Status);
        return description ?? failure ??
            (status is not ("repaired" or "faulty" or "awaiting_parts")
                ? "Статус должен быть repaired, faulty или awaiting_parts."
                : status == "awaiting_parts" &&
                  Clean(request.RequiredParts).Length == 0
                    ? "Для статуса awaiting_parts укажите необходимые запчасти."
                    : null);
    }
}
