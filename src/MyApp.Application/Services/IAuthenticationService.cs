using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции аутентификации.</summary>
public interface IAuthenticationService
{
    /// <summary>Проверяет учётные данные и возвращает результат входа с токеном доступа.</summary>
    Task<ServiceResult<AuthResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken);
}
