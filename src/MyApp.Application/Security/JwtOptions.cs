namespace MyApp.Application.Security;

/// <summary>Параметры подписи и проверки токенов JWT.</summary>
public sealed record JwtOptions(string Key, string Issuer, string Audience);
