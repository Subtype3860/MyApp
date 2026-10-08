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
    public async Task<ServiceResult<bool>> UpdatePartsRequestAsync(
        Guid workId,
        VehiclePartsRequest request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(
                userId, ["Старший механик"], cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }
        var errors = new Dictionary<string, string[]>();
        if (Clean(request.RequestNumber).Length is < 1 or > 100)
        {
            errors[nameof(request.RequestNumber)] =
                ["Укажите номер заявки длиной не более 100 символов."];
        }
        if (request.Content is { Length: > MaximumRequestFileSize })
        {
            errors["file"] = ["Размер файла не должен превышать 20 МБ."];
        }
        if (request.Content is { Length: > 0 } &&
            string.IsNullOrWhiteSpace(request.FileName))
        {
            errors["file"] = ["Укажите имя файла."];
        }
        if (errors.Count > 0)
        {
            return ServiceResult<bool>.Validation(errors);
        }
        var updated = await repository.UpdatePartsRequestAsync(
            workId,
            request with
            {
                RequestNumber = Clean(request.RequestNumber),
                FileName = request.FileName is null
                    ? null
                    : SafeFileName(request.FileName)
            },
            cancellationToken);
        return updated
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.Conflict(
                "Заявку можно добавить только к работе со статусом awaiting_parts.");
    }

    public Task<VehicleRequestFileContent?> GetPartsRequestFileAsync(
        Guid workId,
        CancellationToken cancellationToken) =>
        repository.GetPartsRequestFileAsync(workId, cancellationToken);

    public async Task<ServiceResult<bool>> DeletePartsRequestAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await HasProfessionAsync(userId, ["Старший механик"], cancellationToken))
        {
            return ServiceResult<bool>.Unauthorized();
        }
        return await repository.DeletePartsRequestAsync(workId, cancellationToken)
            ? ServiceResult<bool>.Success(true)
            : ServiceResult<bool>.NotFound();
    }

    private static string SafeFileName(string value)
    {
        var name = value.Replace('\\', '/').Split('/').Last();
        name = new string(name.Where(character => !char.IsControl(character)).ToArray());
        return name.Length <= 255 ? name : name[..255];
    }
}
