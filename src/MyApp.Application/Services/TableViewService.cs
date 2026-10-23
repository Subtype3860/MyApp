using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции просмотра табличных данных.</summary>
public sealed class TableViewService(
    ITableViewRepository tableViewRepository) : ITableViewService
{
    private static readonly HashSet<string> AllowedViews =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "v_full_ost",
            "v_meh_ost",
            "v_workers"
        };

    private static readonly HashSet<string> AllowedPageSizes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "20",
            "40",
            "60",
            "all"
        };

    /// <summary>Возвращает данные табличного представления.</summary>
    public async Task<ServiceResult<TableViewResponse>> GetAsync(
        TableViewRequest request,
        CancellationToken cancellationToken)
    {
        if (!AllowedViews.Contains(request.TableName))
        {
            return ServiceResult<TableViewResponse>.NotFound();
        }

        if (!AllowedPageSizes.Contains(request.PageSize))
        {
            return ServiceResult<TableViewResponse>.BadRequest(
                "Допустимые размеры страницы: 20, 40, 60 или all.");
        }

        var normalizedRequest = request with
        {
            TableName = request.TableName.ToLowerInvariant(),
            Page = Math.Max(request.Page, 1),
            Search = Normalize(request.Search),
            LastName = Normalize(request.LastName),
            FirstName = Normalize(request.FirstName),
            Patronymic = Normalize(request.Patronymic),
            Profession = Normalize(request.Profession)
        };
        var response = await tableViewRepository.QueryAsync(
            normalizedRequest,
            cancellationToken);
        return ServiceResult<TableViewResponse>.Success(response);
    }

    /// <summary>Нормализует параметры поиска и страницы табличного представления.</summary>
    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
