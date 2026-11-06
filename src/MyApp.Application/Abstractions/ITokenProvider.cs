using MyApp.Application.Models;

namespace MyApp.Application.Abstractions;

/// <summary>Контракт выпуска токена доступа для пользователя.</summary>
public interface ITokenProvider
{
    /// <summary>Создаёт токен доступа для указанного пользователя.</summary>
    string Create(User user);
}
