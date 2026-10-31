using MyApp.Application.DTO;

namespace MyApp.Application.Abstractions;

/// <summary>Доступ к данным просмотра табличных данных.</summary>
public interface ITableViewRepository
{
    /// <summary>Получает страницу табличных данных с поиском, сортировкой и фильтрами.</summary>
    Task<TableViewResponse> QueryAsync(
        TableViewRequest request,
        CancellationToken cancellationToken);
}
