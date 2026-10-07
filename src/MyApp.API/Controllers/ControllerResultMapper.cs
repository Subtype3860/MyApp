using MyApp.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.API.Controllers;

internal static class ControllerResultMapper
{
    public static IActionResult ToActionResult<T>(
        this ControllerBase controller,
        ServiceResult<T> result,
        Func<T, IActionResult> onSuccess)
    {
        return result.Status switch
        {
            ServiceResultStatus.Success when result.Value is not null => onSuccess(result.Value),
            ServiceResultStatus.Success => controller.Ok(),
            ServiceResultStatus.Unauthorized => controller.Unauthorized(),
            ServiceResultStatus.NotFound => controller.NotFound(),
            ServiceResultStatus.Conflict => controller.Conflict(new { message = result.Message }),
            ServiceResultStatus.BadRequest => controller.BadRequest(result.Message),
            ServiceResultStatus.ValidationError => controller.BadRequest(
                new ValidationProblemDetails(new Dictionary<string, string[]>(
                    result.Errors ?? throw new InvalidOperationException(
                        "A validation result must contain errors.")))),
            _ => throw new InvalidOperationException(
                $"Unsupported service result status '{result.Status}'.")
        };
    }
}
