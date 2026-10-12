using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;

namespace MyApp.Application.Services;

public sealed class ComponentDocumentService(
    IUserRepository userRepository,
    IResponsibleEmployeeService responsibleEmployeeService,
    IRequirementJournalRepository requirementJournalRepository,
    IMaterialGroupService materialGroupService) :
    IComponentDocumentService
{
    private const int MaximumItems = 100;

    public Task<ServiceResult<ComponentDocumentResponse>> PrepareAsync(
        ComponentDocumentRequest request,
        Guid userId,
        CancellationToken cancellationToken) =>
        ProcessAsync(request, userId, false, cancellationToken);

    public Task<ServiceResult<ComponentDocumentResponse>> GenerateAsync(
        ComponentDocumentRequest request,
        Guid userId,
        CancellationToken cancellationToken) =>
        ProcessAsync(request, userId, true, cancellationToken);

    private async Task<ServiceResult<ComponentDocumentResponse>> ProcessAsync(
        ComponentDocumentRequest request,
        Guid userId,
        bool saveRequirement,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return ServiceResult<ComponentDocumentResponse>.Unauthorized();
        }
        var profession = user.Profession;
        if (profession is null || !string.Equals(profession.Name.Trim(), "Механик", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<ComponentDocumentResponse>.Forbidden(
                "Создавать складское требование может только механик.");
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return ServiceResult<ComponentDocumentResponse>.Validation(errors);
        }

        var materialGroupMappings = await materialGroupService.GetMappingsAsync(
            request.SourceTable,
            cancellationToken);
        if (materialGroupMappings.Status != ServiceResultStatus.Success ||
            materialGroupMappings.Value is null)
        {
            return new ServiceResult<ComponentDocumentResponse>(
                materialGroupMappings.Status,
                Message: materialGroupMappings.Message,
                Errors: materialGroupMappings.Errors);
        }

        var responsibleEmployee = await responsibleEmployeeService.ResolveAsync(
            request.SourceTable,
            request.ResponsibleEmployee,
            cancellationToken);
        if (responsibleEmployee.Status != ServiceResultStatus.Success ||
            responsibleEmployee.Value is null)
        {
            return new ServiceResult<ComponentDocumentResponse>(
                responsibleEmployee.Status,
                Message: responsibleEmployee.Message,
                Errors: responsibleEmployee.Errors);
        }

        var normalizedItems = request.Items
            .Select(item => item with
            {
                Name = item.Name.Trim(),
                Unit = item.Unit.Trim()
            })
            .ToArray();
        var layout = BuildDocumentLayout(
            normalizedItems,
            NormalizeVehicleNumbers(request.VehicleNumber, normalizedItems.Length),
            materialGroupMappings.Value);
        var normalizedRequest = request with
        {
            VehicleNumber = request.VehicleNumber.Trim(),
            JobName = string.IsNullOrWhiteSpace(request.JobName)
                ? "Аварийная"
                : request.JobName.Trim(),
            VehicleNumbers = layout.VehicleNumbers,
            Pages = layout.Pages,
            AuthorPosition = profession.Name,
            AuthorName = BuildAuthorName(
                user.FirstName,
                user.MiddleName,
                user.LastName),
            IssuerPosition = responsibleEmployee.Value.Profession,
            IssuerName = BuildAuthorName(
                responsibleEmployee.Value.FirstName,
                responsibleEmployee.Value.Patronymic,
                responsibleEmployee.Value.LastName),
            Items = layout.Items
        };
        if (saveRequirement)
        {
            await requirementJournalRepository.SaveAsync(
                user.Id,
                BuildFullName(user.FirstName, user.MiddleName, user.LastName),
                BuildFullName(
                    responsibleEmployee.Value.FirstName,
                    responsibleEmployee.Value.Patronymic,
                    responsibleEmployee.Value.LastName),
                normalizedRequest,
                cancellationToken);
        }
        return ServiceResult<ComponentDocumentResponse>.Success(
            new ComponentDocumentResponse(normalizedRequest));
    }

    private static string BuildAuthorName(
        string firstName,
        string middleName,
        string lastName)
    {
        var initials = new[] { firstName, middleName }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => $"{name.Trim()[0]}.");
        return $"{string.Concat(initials)} {lastName.Trim()}".Trim();
    }

    private static string BuildFullName(
        string firstName,
        string middleName,
        string lastName) =>
        string.Join(
            ' ',
            new[] { lastName, firstName, middleName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));

    private static IReadOnlyList<string> NormalizeVehicleNumbers(
        string vehicleNumber,
        int itemCount)
    {
        var values = ParseVehicleNumbers(vehicleNumber);
        if (values.Length <= 1)
        {
            return values;
        }

        return Enumerable.Range(0, itemCount)
            .Select(index => values[Math.Min(index, values.Length - 1)])
            .ToArray();
    }

    private static DocumentLayout BuildDocumentLayout(
        IReadOnlyList<ComponentDocumentItem> items,
        IReadOnlyList<string> vehicleNumbers,
        IReadOnlyList<MaterialGroupMappingResponse> mappings)
    {
        var mappingByName = mappings.ToDictionary(
            mapping => mapping.MaterialName.Trim(),
            StringComparer.Ordinal);
        var groups = new Dictionary<Guid, List<IndexedDocumentItem>>();
        var groupOrder = new List<Guid>();
        var ungrouped = new List<IndexedDocumentItem>();

        for (var index = 0; index < items.Count; index++)
        {
            var indexedItem = new IndexedDocumentItem(
                items[index],
                vehicleNumbers.Count > 1 ? vehicleNumbers[index] : null);
            if (!mappingByName.TryGetValue(items[index].Name, out var mapping))
            {
                ungrouped.Add(indexedItem);
                continue;
            }

            if (!groups.TryGetValue(mapping.GroupId, out var groupItems))
            {
                groupItems = [];
                groups[mapping.GroupId] = groupItems;
                groupOrder.Add(mapping.GroupId);
            }
            groupItems.Add(indexedItem);
        }

        var pages = new List<IReadOnlyList<ComponentDocumentItem>>();
        var orderedItems = new List<ComponentDocumentItem>(items.Count);
        var orderedVehicleNumbers = new List<string>(items.Count);

        foreach (var groupId in groupOrder)
        {
            AddPages(
                groups[groupId],
                pages,
                orderedItems,
                orderedVehicleNumbers);
        }
        AddPages(ungrouped, pages, orderedItems, orderedVehicleNumbers);

        return new DocumentLayout(
            orderedItems,
            vehicleNumbers.Count > 1
                ? orderedVehicleNumbers
                : vehicleNumbers,
            pages);
    }

    private static void AddPages(
        IReadOnlyList<IndexedDocumentItem> items,
        ICollection<IReadOnlyList<ComponentDocumentItem>> pages,
        ICollection<ComponentDocumentItem> orderedItems,
        ICollection<string> orderedVehicleNumbers)
    {
        foreach (var chunk in items.Chunk(5))
        {
            var page = chunk.Select(entry => entry.Item).ToArray();
            pages.Add(page);
            foreach (var entry in chunk)
            {
                orderedItems.Add(entry.Item);
                if (entry.VehicleNumber is not null)
                {
                    orderedVehicleNumbers.Add(entry.VehicleNumber);
                }
            }
        }
    }

    private static string[] ParseVehicleNumbers(string value) =>
        value.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

    private static Dictionary<string, string[]> Validate(
        ComponentDocumentRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.VehicleNumber))
        {
            errors[nameof(request.VehicleNumber)] = ["Укажите номер техники."];
        }

        var vehicleNumbers = ParseVehicleNumbers(request.VehicleNumber);
        if (vehicleNumbers.Length == 0)
        {
            errors[nameof(request.VehicleNumber)] = ["Укажите номер техники."];
        }
        else if (vehicleNumbers.Length > request.Items.Count)
        {
            errors[nameof(request.VehicleNumber)] =
                ["Количество номеров техники не должно превышать количество наименований."];
        }

        if (request.ResponsibleEmployee is null)
        {
            errors[nameof(request.ResponsibleEmployee)] =
                ["Выберите ответственное лицо за выдачу."];
        }

        if (request.Items.Count == 0)
        {
            errors[nameof(request.Items)] = ["Выберите хотя бы один компонент."];
            return errors;
        }

        if (request.Items.Count > MaximumItems)
        {
            errors[nameof(request.Items)] =
                [$"Можно сформировать не более {MaximumItems} компонентов за один раз."];
        }

        if (request.JobName?.Trim().Length > 100)
        {
            errors[nameof(request.JobName)] =
                ["Наименование ТО не должно превышать 100 символов."];
        }

        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.Name)))
        {
            errors[nameof(ComponentDocumentItem.Name)] =
                ["У каждого компонента должно быть наименование."];
        }

        if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.Unit)))
        {
            errors[nameof(ComponentDocumentItem.Unit)] =
                ["У каждого компонента должна быть единица измерения."];
        }

        if (request.Items.Any(item => item.Quantity <= 0))
        {
            errors[nameof(ComponentDocumentItem.Quantity)] =
                ["Количество должно быть больше нуля."];
        }

        if (request.Items.Any(item =>
                item.AvailableQuantity < 0 ||
                item.Quantity > item.AvailableQuantity))
        {
            errors[nameof(ComponentDocumentItem.AvailableQuantity)] =
                ["Количество к списанию не может превышать остаток."];
        }

        return errors;
    }

    private sealed record IndexedDocumentItem(
        ComponentDocumentItem Item,
        string? VehicleNumber);

    private sealed record DocumentLayout(
        IReadOnlyList<ComponentDocumentItem> Items,
        IReadOnlyList<string> VehicleNumbers,
        IReadOnlyList<IReadOnlyList<ComponentDocumentItem>> Pages);
}
