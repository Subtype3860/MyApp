namespace MyApp.Application.DTO {
    /// <summary>Логин и пароль для входа пользователя.</summary>
    public record LoginRequest(string UserNameOrEmail, string Password);
    /// <summary>Данные успешной аутентификации, возвращаемые клиенту.</summary>
    public record AuthResponse(string Token, string Role, IReadOnlyList<string> Permissions);
    /// <summary>Данные для создания учётной записи пользователя.</summary>
    public record CreateUserRequest(
        string FirstName,
        string MiddleName,
        string LastName,
        string UserName,
        string Password,
        Guid ProfessionId);
    /// <summary>Изменения учётной записи, роли и прав пользователя.</summary>
    public record UpdateUserRequest(
        string FirstName,
        string MiddleName,
        string LastName,
        string UserName,
        string? Password,
        Guid ProfessionId,
        string Role,
        IReadOnlyList<string>? Permissions);
}
