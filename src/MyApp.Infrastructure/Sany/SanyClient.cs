using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MyApp.Infrastructure.Sany;

/// <summary>Настройки доступа к порталу SANY SIS.</summary>
public sealed record SanyOptions(string BaseUrl = "https://sis-global.sanygroup.com/api-gateway")
{
    /// <summary>Базовый адрес API-шлюза без завершающей косой черты.</summary>
    public string NormalizedBaseUrl => BaseUrl.TrimEnd('/');
}

/// <summary>Капча и её идентификатор из ответа портала.</summary>
public sealed record SanyCaptcha(byte[] Image, string CaptchaId);

/// <summary>Результат входа в портал.</summary>
public sealed record SanyLoginResult(bool Ok, string? Token, string? Message);

/// <summary>Изолированное подключение к порталу с собственными cookies.</summary>
public sealed class SanyConnection : IDisposable
{
    public SanyConnection(string id)
    {
        Id = id;
        Cookies = new CookieContainer();
        Handler = new HttpClientHandler { CookieContainer = Cookies, UseCookies = true };
        Client = new HttpClient(Handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string Id { get; }
    public CookieContainer Cookies { get; }
    public HttpClientHandler Handler { get; }
    public HttpClient Client { get; }
    public string? CaptchaId { get; set; }
    public string? Account { get; set; }
    public string? Password { get; set; }
    public string? Token { get; set; }
    public string? UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Connected => !string.IsNullOrWhiteSpace(Token);

    public void Dispose()
    {
        Client.Dispose();
        Handler.Dispose();
    }
}

/// <summary>HTTP-клиент портала SANY SIS: капча и вход по логину и паролю.</summary>
public sealed class SanyClient(SanyOptions options)
{
    /// <summary>Запрашивает капчу и сохраняет её идентификатор в подключении.</summary>
    public async Task<SanyCaptcha> GetCaptchaAsync(SanyConnection connection, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{options.NormalizedBaseUrl}/customer/user/v2/kaptcha.jpg");
        request.Headers.TryAddWithoutValidation("Origin", "https://sis-global.sanygroup.com");
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36");
        using var response = await connection.Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        // Заголовок kaptchaid не обязателен: портал проверяет ответ капчи по своему состоянию.
        var captchaId = response.Headers.TryGetValues("kaptchaid", out var values)
            ? values.FirstOrDefault()?.Trim() ?? ""
            : "";
        connection.CaptchaId = captchaId;
        return new SanyCaptcha(await response.Content.ReadAsByteArrayAsync(cancellationToken), captchaId);
    }

    /// <summary>Выполняет вход в портал с введённым кодом капчи.</summary>
    public async Task<SanyLoginResult> LoginAsync(SanyConnection connection, string captchaCode,
        int tokenHours, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.Account) || string.IsNullOrWhiteSpace(connection.Password))
            return new SanyLoginResult(false, null, "Не указаны логин и пароль SANY.");

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{options.NormalizedBaseUrl}/customer/user/onlogin?captchaCode={Uri.EscapeDataString(captchaCode.Trim())}&language=ru");
        if (!string.IsNullOrWhiteSpace(connection.CaptchaId))
            request.Headers.TryAddWithoutValidation("captchaCodeId", connection.CaptchaId);
        request.Headers.TryAddWithoutValidation("Origin", "https://sis-global.sanygroup.com");
        request.Content = JsonContent.Create(new { account = connection.Account, password = connection.Password });
        using var response = await connection.Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new SanyLoginResult(false, null, "Портал SANY отклонил вход. Проверьте логин, пароль и код с картинки.");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var token = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("token", out var tokenElement)
            ? tokenElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(token))
        {
            var code = root.TryGetProperty("code", out var codeElement) ? codeElement.ToString() : "";
            var portalMessage = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? ""
                : "";
            var isCaptchaError = portalMessage.Contains("验证码", StringComparison.Ordinal) ||
                                 portalMessage.Contains("подтверждения", StringComparison.OrdinalIgnoreCase);
            var reason = portalMessage.Length > 0 ? $" Ответ портала: {portalMessage} (код {code})." : "";
            var message = isCaptchaError
                ? "Неверный код с картинки. Запросите капчу заново и повторите ввод." + reason
                : code == "3"
                    ? "Портал SANY отклонил логин или пароль. Проверьте данные учётной записи." + reason
                    : "Портал SANY не вернул токен доступа." + reason;
            return new SanyLoginResult(false, null, message);
        }

        connection.Token = token;
        connection.UserId = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("user", out var user) &&
                            user.ValueKind == JsonValueKind.Object && user.TryGetProperty("id", out var idElement)
            ? idElement.ToString()
            : null;
        connection.Password = null;
        connection.ExpiresAt = DateTimeOffset.UtcNow.AddHours(Math.Clamp(tokenHours, 1, 24));
        return new SanyLoginResult(true, token, null);
    }

    /// <summary>Выполняет запрос к разрешённому разделу портала от имени подключения.</summary>
    public async Task<(int Status, string Body)> SendAsync(SanyConnection connection, string path,
        object? body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connection.Token))
            return (401, "{\"message\":\"Нет активного подключения к SANY.\"}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.NormalizedBaseUrl}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", connection.Token);
        if (!string.IsNullOrWhiteSpace(connection.UserId))
            request.Headers.TryAddWithoutValidation("Authorization-userId", connection.UserId);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await connection.Client.SendAsync(request, cancellationToken);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
