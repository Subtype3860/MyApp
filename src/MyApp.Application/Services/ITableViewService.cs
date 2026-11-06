using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции просмотра табличных данных.</summary>
public interface ITableViewService
{
    /// <summary>Возвращает данные табличного представления.</summary>
    Task<ServiceResult<TableViewResponse>> GetAsync(
        TableViewRequest request,
        CancellationToken cancellationToken);
}
