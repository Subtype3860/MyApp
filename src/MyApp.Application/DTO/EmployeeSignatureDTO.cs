namespace MyApp.Application.DTO;

/// <summary>Данные сотрудника, по которым определяется принадлежность подписи.</summary>
public sealed record EmployeeSignatureKey(
    string LastName,
    string FirstName,
    string Patronymic);

/// <summary>Содержимое сохранённой подписи сотрудника и сведения о формате.</summary>
public sealed record EmployeeSignatureResponse(
    byte[] Content,
    string ContentType);
