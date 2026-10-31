namespace MyApp.Application.Abstractions;

/// <summary>Контракт инициализации структуры и исходных данных базы.</summary>
public interface IDatabaseInitializer
{
    /// <summary>Инициализирует структуру и исходные данные хранилища.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
