using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Models;

namespace MyApp.Application.Services;

/// <summary>Прикладные операции справочника профессий.</summary>
public sealed class ProfessionService(
    IProfessionRepository professionRepository) : IProfessionService
{
    /// <summary>Возвращает список профессий.</summary>
    public async Task<IReadOnlyList<ProfessionResponse>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var professions = await professionRepository.GetAllAsync(
            cancellationToken);
        return professions
            .Select(item => new ProfessionResponse(item.Id, item.Name))
            .ToArray();
    }

    /// <summary>Создаёт профессию.</summary>
    public async Task<ServiceResult<ProfessionResponse>> CreateAsync(
        ProfessionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<ProfessionResponse>.Validation(errors);
        }

        var profession = new Profession
        {
            Id = Guid.NewGuid(),
            Name = request.Profession.Trim()
        };
        await professionRepository.AddAsync(profession, cancellationToken);
        await professionRepository.SaveChangesAsync(cancellationToken);

        return ServiceResult<ProfessionResponse>.Success(
            new ProfessionResponse(profession.Id, profession.Name));
    }

    /// <summary>Обновляет данные профессии.</summary>
    public async Task<ServiceResult<ProfessionResponse>> UpdateAsync(
        Guid id,
        ProfessionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<ProfessionResponse>.Validation(errors);
        }

        var profession = await professionRepository.FindByIdAsync(
            id,
            cancellationToken);
        if (profession is null)
        {
            return ServiceResult<ProfessionResponse>.NotFound();
        }

        profession.Name = request.Profession.Trim();
        await professionRepository.SaveChangesAsync(cancellationToken);

        return ServiceResult<ProfessionResponse>.Success(
            new ProfessionResponse(profession.Id, profession.Name));
    }

    /// <summary>Удаляет профессию.</summary>
    public async Task<ServiceResult<Guid>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var profession = await professionRepository.FindByIdAsync(
            id,
            cancellationToken);
        if (profession is null)
        {
            return ServiceResult<Guid>.NotFound();
        }

        if (await professionRepository.IsAssignedAsync(id, cancellationToken))
        {
            return ServiceResult<Guid>.Conflict(
                "Профессия назначена пользователям и не может быть удалена.");
        }

        professionRepository.Remove(profession);
        await professionRepository.SaveChangesAsync(cancellationToken);
        return ServiceResult<Guid>.Success(id);
    }

    /// <summary>Проверяет обязательность названия профессии и ограничение длины в 40 символов.</summary>
    private static Dictionary<string, string[]> Validate(
        ProfessionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Profession))
        {
            return new Dictionary<string, string[]>
            {
                [nameof(request.Profession)] = ["Укажите название профессии."]
            };
        }

        if (request.Profession.Trim().Length > 40)
        {
            return new Dictionary<string, string[]>
            {
                [nameof(request.Profession)] =
                    ["Название не должно превышать 40 символов."]
            };
        }

        return [];
    }
}
