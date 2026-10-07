namespace MyApp.Application.DTO;

public sealed record EmployeeSignatureKey(Guid EmployeeId);

public sealed record EmployeeSignatureResponse(
    byte[] Content,
    string ContentType);
