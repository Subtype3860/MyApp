using System.Security.Cryptography;
using MyApp.Application.Abstractions;

namespace MyApp.Infrastructure.Security;

/// <summary>Хеширование паролей с помощью PBKDF2 и проверка их соответствия.</summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    /// <summary>Вычисляет хеш пароля для хранения вместо исходного текста.</summary>
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return $"{Algorithm}${Iterations}${Convert.ToBase64String(salt)}" +
            $"${Convert.ToBase64String(hash)}";
    }

    /// <summary>Проверяет соответствие пароля сохранённому хешу.</summary>
    public bool Verify(string password, string encodedHash)
    {
        var parts = encodedHash.Split('$');
        if (parts.Length != 4 ||
            parts[0] != Algorithm ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return expectedHash.Length == actualHash.Length &&
                CryptographicOperations.FixedTimeEquals(
                    expectedHash,
                    actualHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
