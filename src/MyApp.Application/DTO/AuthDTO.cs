namespace MyApp.Application.DTO {
    public record LoginRequest(string UserNameOrEmail, string Password);
    public record AuthResponse(string Token, string Role, IReadOnlyList<string> Permissions);
    public record CreateUserRequest(
        string FirstName,
        string MiddleName,
        string LastName,
        string UserName,
        string Password,
        Guid ProfessionId);
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
