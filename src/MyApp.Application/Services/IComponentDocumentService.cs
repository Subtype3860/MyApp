using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции документов требований на материалы.</summary>
public interface IComponentDocumentService
{
    /// <summary>Проверяет запрос и подготавливает данные предварительного просмотра требования.</summary>
    Task<ServiceResult<ComponentDocumentResponse>> PrepareAsync(
        ComponentDocumentRequest request,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>Формирует данные документа требования по выбранным материалам.</summary>
    Task<ServiceResult<ComponentDocumentResponse>> GenerateAsync(
        ComponentDocumentRequest request,
        Guid userId,
        CancellationToken cancellationToken);
}
