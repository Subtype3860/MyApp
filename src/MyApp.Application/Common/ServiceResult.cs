namespace MyApp.Application.Common;

/// <summary>Возможные исходы выполнения прикладной операции.</summary>
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

/// <summary>Результат операции с данными, ошибками проверки и статусом выполнения.</summary>
public sealed record ServiceResult<T>(
    ServiceResultStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    /// <summary>Создаёт успешный результат операции с возвращаемым значением.</summary>
    public static ServiceResult<T> Success(T value) =>
        new(ServiceResultStatus.Success, value);

    /// <summary>Создаёт результат с ошибками проверки полей запроса.</summary>
    public static ServiceResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(ServiceResultStatus.ValidationError, Errors: errors);

    /// <summary>Создаёт результат конфликта с текущим состоянием данных.</summary>
    public static ServiceResult<T> Conflict(string message) =>
        new(ServiceResultStatus.Conflict, Message: message);

    /// <summary>Создаёт результат отклонения некорректного запроса.</summary>
    public static ServiceResult<T> BadRequest(string message) =>
        new(ServiceResultStatus.BadRequest, Message: message);

    /// <summary>Создаёт результат отсутствия запрошенного объекта.</summary>
    public static ServiceResult<T> NotFound() =>
        new(ServiceResultStatus.NotFound);

    /// <summary>Создаёт результат отказа из-за недостаточных прав доступа.</summary>
    public static ServiceResult<T> Forbidden(string message) =>
        new(ServiceResultStatus.Forbidden, Message: message);

    /// <summary>Создаёт результат отказа для неаутентифицированного пользователя.</summary>
    public static ServiceResult<T> Unauthorized() =>
        new(ServiceResultStatus.Unauthorized);
}
