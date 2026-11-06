namespace MyApp.Application.DTO;

/// <summary>Данные сотрудника для выбора ответственного.</summary>
public sealed record ResponsibleEmployeeResponse(
    string FirstName,
    string Patronymic,
    string LastName,
    string Profession);

/// <summary>Выбранные ответственные сотрудники документа.</summary>
public sealed record ResponsibleEmployeeSelection(
    string FirstName,
    string Patronymic,
    string LastName);
