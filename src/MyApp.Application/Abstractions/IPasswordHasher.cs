namespace MyApp.Application.Abstractions;

/// <summary>Контракт хеширования паролей и проверки сохранённых хешей.</summary>
public interface IPasswordHasher
{
    /// <summary>Вычисляет хеш пароля для хранения вместо исходного текста.</summary>
    string Hash(string password);

    /// <summary>Проверяет соответствие пароля сохранённому хешу.</summary>
    bool Verify(string password, string encodedHash);
}
