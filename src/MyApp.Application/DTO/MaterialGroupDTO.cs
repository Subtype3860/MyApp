namespace MyApp.Application.DTO;

/// <summary>Данные для создания группы материалов.</summary>
public sealed record MaterialGroupRequest(string Name);

/// <summary>Материал, включаемый в группу.</summary>
public sealed record MaterialGroupItemRequest(
    string SourceTable,
    string MaterialName);

/// <summary>Материал группы в ответе клиенту.</summary>
public sealed record MaterialGroupItemResponse(
    Guid Id,
    string SourceTable,
    string MaterialName);

/// <summary>Группа и входящие в неё материалы.</summary>
public sealed record MaterialGroupResponse(
    Guid Id,
    string Name,
    IReadOnlyList<MaterialGroupItemResponse> Items);

/// <summary>Соответствие материала группе для отображения на клиенте.</summary>
public sealed record MaterialGroupMappingResponse(
    string MaterialName,
    Guid GroupId,
    string GroupName);
