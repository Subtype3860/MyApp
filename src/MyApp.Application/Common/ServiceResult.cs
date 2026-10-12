namespace MyApp.Application.Common;

public enum ServiceResultStatus
{
    Success,
    ValidationError,
    BadRequest,
    Conflict,
    NotFound,
    Unauthorized,
    Forbidden
}

public sealed record ServiceResult<T>(
    ServiceResultStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public static ServiceResult<T> Success(T value) =>
        new(ServiceResultStatus.Success, value);

    public static ServiceResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(ServiceResultStatus.ValidationError, Errors: errors);

    public static ServiceResult<T> Conflict(string message) =>
        new(ServiceResultStatus.Conflict, Message: message);

    public static ServiceResult<T> BadRequest(string message) =>
        new(ServiceResultStatus.BadRequest, Message: message);

    public static ServiceResult<T> NotFound() =>
        new(ServiceResultStatus.NotFound);

    public static ServiceResult<T> Forbidden(string message) =>
        new(ServiceResultStatus.Forbidden, Message: message);

    public static ServiceResult<T> Unauthorized() =>
        new(ServiceResultStatus.Unauthorized);
}
