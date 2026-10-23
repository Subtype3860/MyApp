using System.Reflection;
using MyApp.Application.Abstractions;
using MyApp.Application.Common;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Application.Models;
using Xunit;

namespace MyApp.Infrastructure.Tests;

/// <summary>Тесты прав участия в работах и управления парком.</summary>
public sealed class WorkParticipationAuthorizationTests
{
    private readonly Guid workId = Guid.NewGuid();
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid participantId = Guid.NewGuid();

    /// <summary>Создаёт пользователя с заданной ролью и профессией для теста.</summary>
    private static User MakeUser(Guid id, string profession, string role = "user") => new() {
        Id = id, FirstName = "Иван", MiddleName = "Иванович", LastName = "Иванов",
        UserName = "worker", Email = "worker@example.test", PasswordHash = "test", Role = role,
        Profession = new Profession { Id = Guid.NewGuid(), Name = profession }
    };

    /// <summary>Создаёт сервис с тестовыми зависимостями.</summary>
    private VehicleService MakeService(User? user, Func<MethodInfo, object?[], object?> repositoryHandler)
    {
        var users = DispatchProxy.Create<IUserRepository, InvocationProxy>();
        ((InvocationProxy)(object)users).Handler = (method, _) => method.Name == nameof(IUserRepository.FindByIdAsync)
            ? Task.FromResult(user) : throw new InvalidOperationException(method.Name);
        var repository = DispatchProxy.Create<IVehicleRepository, InvocationProxy>();
        ((InvocationProxy)(object)repository).Handler = repositoryHandler;
        return new VehicleService(repository, users);
    }

    /// <summary>Проверяет обязательность профессии механика для согласования участия, в том числе у администратора.</summary>
    [Theory]
    [InlineData("Слесарь", "user")]
    [InlineData("Электрослесарь", "user")]
    [InlineData("Слесарь", "administrator")]
    public async Task Only_a_mechanic_may_review_even_if_user_is_administrator(string profession, string role)
    {
        var service = MakeService(MakeUser(userId, profession, role), (method, _) => throw new InvalidOperationException("Repository must not be called"));
        var result = await service.ReviewWorkParticipationAsync(workId, participantId, true, userId, default);
        Assert.Equal(ServiceResultStatus.BadRequest, result.Status);
    }

    /// <summary>Проверяет связь решения с текущим механиком и конкретной работой.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mechanic_decision_uses_authenticated_reviewer_and_exact_work(bool approved)
    {
        var reviewed = false;
        var service = MakeService(MakeUser(userId, "Механик"), (method, args) => {
            if (method.Name == nameof(IVehicleRepository.WorkExistsAsync)) return Task.FromResult(true);
            Assert.Equal(nameof(IVehicleRepository.ReviewWorkParticipationAsync), method.Name);
            Assert.Equal(workId, args[0]); Assert.Equal(participantId, args[1]);
            Assert.Equal(approved, args[2]); Assert.Equal(userId, args[3]);
            reviewed = true;
            return Task.FromResult(true);
        });
        Assert.Equal(ServiceResultStatus.Success, (await service.ReviewWorkParticipationAsync(workId, participantId, approved, userId, default)).Status);
        Assert.True(reviewed);
    }

    /// <summary>Проверяет регистрацию участия от имени текущего сотрудника.</summary>
    [Fact]
    public async Task Self_registration_uses_the_authenticated_employee()
    {
        var registered = false;
        var service = MakeService(MakeUser(userId, "Слесарь"), (method, args) => {
            if (method.Name == nameof(IVehicleRepository.WorkExistsAsync)) return Task.FromResult(true);
            Assert.Equal(nameof(IVehicleRepository.RequestWorkParticipationAsync), method.Name);
            Assert.Equal(workId, args[0]); Assert.Equal(userId, args[1]);
            registered = true;
            return Task.FromResult(true);
        });
        Assert.Equal(ServiceResultStatus.Success, (await service.RequestWorkParticipationAsync(workId, userId, default)).Status);
        Assert.True(registered);
    }

    /// <summary>Проверяет конфликт при повторной заявке на участие.</summary>
    [Fact]
    public async Task Duplicate_registration_returns_conflict()
    {
        var service = MakeService(MakeUser(userId, "Слесарь"), (method, _) => Task.FromResult(method.Name == nameof(IVehicleRepository.WorkExistsAsync)));
        Assert.Equal(ServiceResultStatus.Conflict, (await service.RequestWorkParticipationAsync(workId, userId, default)).Status);
    }

    /// <summary>Проверяет запрет повторного согласования и обработки заявки другой работы.</summary>
    [Fact]
    public async Task Already_reviewed_or_wrong_work_cannot_be_reviewed_again()
    {
        var service = MakeService(MakeUser(userId, "Механик"), (method, _) => Task.FromResult(method.Name == nameof(IVehicleRepository.WorkExistsAsync)));
        Assert.Equal(ServiceResultStatus.Conflict, (await service.ReviewWorkParticipationAsync(workId, participantId, true, userId, default)).Status);
    }

    /// <summary>Проверяет запрет регистрации участия без пользователя сессии.</summary>
    [Fact]
    public async Task Missing_session_user_cannot_register()
    {
        var service = MakeService(null, (method, _) => throw new InvalidOperationException("Repository must not be called"));
        Assert.Equal(ServiceResultStatus.Unauthorized, (await service.RequestWorkParticipationAsync(workId, userId, default)).Status);
    }

    /// <summary>Проверяет создание вспомогательной работы сотрудником без ремонтной профессии.</summary>
    [Fact]
    public async Task Auxiliary_work_can_be_recorded_by_an_employee_without_a_repair_profession()
    {
        var service = MakeService(MakeUser(userId, "Разнорабочий"), (method, args) => {
            Assert.Equal(nameof(IVehicleRepository.AddAuxiliaryWorkAsync), method.Name);
            var request = Assert.IsType<AuxiliaryWorkRequest>(args[0]);
            Assert.Equal("Уборка территории", request.Title);
            Assert.Equal("РММ", request.Location);
            Assert.Equal(userId, args[1]);
            return Task.FromResult(workId);
        });
        var result = await service.AddAuxiliaryWorkAsync(new(" Уборка территории ", " Собран мусор ", " РММ ", DateTimeOffset.UtcNow.AddMinutes(-20)), userId, default);
        Assert.Equal(ServiceResultStatus.Success, result.Status);
        Assert.Equal(workId, result.Value);
    }

    /// <summary>Проверяет обязательность описания, места и допустимого времени вспомогательной работы.</summary>
    [Theory]
    [InlineData("", "Описание", "РММ", false)]
    [InlineData("Уборка", "", "РММ", false)]
    [InlineData("Уборка", "Описание", "", false)]
    [InlineData("Уборка", "Описание", "РММ", true)]
    public async Task Auxiliary_work_requires_description_location_and_valid_time(string title, string description, string location, bool future)
    {
        var service = MakeService(MakeUser(userId, "Разнорабочий"), (method, _) => throw new InvalidOperationException("Invalid work must not be saved"));
        var result = await service.AddAuxiliaryWorkAsync(new(title, description, location, DateTimeOffset.UtcNow.AddHours(future ? 2 : -2)), userId, default);
        Assert.Equal(ServiceResultStatus.ValidationError, result.Status);
    }

    /// <summary>Проверяет загрузку вложений вспомогательной работы только её владельцем.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Auxiliary_media_upload_requires_ownership_even_for_a_non_repair_employee(bool owner)
    {
        var saved = false;
        var service = MakeService(MakeUser(userId, "Разнорабочий"), (method, _) => {
            if (method.Name is nameof(IVehicleRepository.WorkExistsAsync) or nameof(IVehicleRepository.AuxiliaryWorkExistsAsync)) return Task.FromResult(true);
            if (method.Name == nameof(IVehicleRepository.IsWorkCreatorAsync)) return Task.FromResult(owner);
            if (method.Name == nameof(IVehicleRepository.GetWorkPhotoCountAsync)) return Task.FromResult(0);
            if (method.Name == nameof(IVehicleRepository.AddWorkPhotosAsync)) { saved = true; return Task.FromResult<IReadOnlyList<Guid>>([Guid.NewGuid()]); }
            throw new InvalidOperationException(method.Name);
        });
        var result = await service.AddWorkPhotosAsync(workId, [new("photo.webp", "image/webp", "RIFF0000WEBP"u8.ToArray())], userId, default);
        Assert.Equal(owner ? ServiceResultStatus.Success : ServiceResultStatus.Unauthorized, result.Status);
        Assert.Equal(owner, saved);
    }

    /// <summary>Проверяет право механиков и администраторов управлять парком техники.</summary>
    [Theory]
    [InlineData("Механик", "user")]
    [InlineData("Старший механик", "user")]
    [InlineData("Администратор", "administrator")]
    public async Task Mechanics_and_administrators_can_manage_the_fleet(string profession, string role)
    {
        var service = MakeService(MakeUser(userId, profession, role), (method, args) => {
            Assert.Equal(nameof(IVehicleRepository.SaveVehicleCatalogAsync), method.Name);
            Assert.Equal(workId, args[0]);
            var request = Assert.IsType<VehicleCatalogRequest>(args[1]);
            Assert.Equal("SANY", request.ModelName);
            Assert.Equal("Спецтехника", request.GroupName);
            return Task.FromResult(ServiceResult<Guid>.Success(workId));
        });
        var result = await service.SaveVehicleCatalogAsync(workId, new(" Спецтехника ", " Экскаваторы ", " SANY ", 94, "", ""), userId, default);
        Assert.Equal(ServiceResultStatus.Success, result.Status);
        Assert.Equal(workId, result.Value);
    }

    /// <summary>Проверяет запрет обычному работнику создавать, изменять и удалять технику.</summary>
    [Fact]
    public async Task A_worker_cannot_create_edit_or_remove_fleet_vehicles()
    {
        var service = MakeService(MakeUser(userId, "Слесарь"), (_, _) => throw new Exception("Worker must not mutate fleet"));
        var request = new VehicleCatalogRequest("Спецтехника", "Экскаваторы", "SANY", 94, "", "");
        Assert.Equal(ServiceResultStatus.Forbidden, (await service.SaveVehicleCatalogAsync(null, request, userId, default)).Status);
        Assert.Equal(ServiceResultStatus.Forbidden, (await service.SaveVehicleCatalogAsync(workId, request, userId, default)).Status);
        Assert.Equal(ServiceResultStatus.Forbidden, (await service.ArchiveVehicleAsync(workId, userId, default)).Status);
    }

    /// <summary>Проверяет отсутствие записи некорректной карточки техники.</summary>
    [Theory]
    [InlineData("", "Экскаваторы", "SANY", 94)]
    [InlineData("Спецтехника", "", "SANY", 94)]
    [InlineData("Спецтехника", "Экскаваторы", "", 94)]
    [InlineData("Спецтехника", "Экскаваторы", "SANY", -1)]
    public async Task Invalid_fleet_data_is_not_written(string group, string type, string model, int garage)
    {
        var service = MakeService(MakeUser(userId, "Механик"), (_, _) => throw new Exception("Invalid data must not be written"));
        Assert.Equal(ServiceResultStatus.ValidationError, (await service.SaveVehicleCatalogAsync(null, new(group, type, model, garage, "", ""), userId, default)).Status);
    }

    /// <summary>Проверяет обязательность хотя бы одного идентификационного номера техники.</summary>
    [Fact]
    public async Task Fleet_vehicle_requires_at_least_one_identifier()
    {
        var service = MakeService(MakeUser(userId, "Механик"), (_, _) => throw new Exception("Invalid data must not be written"));
        Assert.Equal(ServiceResultStatus.ValidationError, (await service.SaveVehicleCatalogAsync(null, new("Спецтехника", "Экскаваторы", "SANY", null, "", ""), userId, default)).Status);
    }

    /// <summary>Проверяет архивирование техники с сохранением её истории.</summary>
    [Fact]
    public async Task Removing_a_vehicle_uses_archival_instead_of_cascading_history_deletion()
    {
        var service = MakeService(MakeUser(userId, "Механик"), (method, args) => {
            Assert.Equal(nameof(IVehicleRepository.ArchiveVehicleAsync), method.Name);
            Assert.Equal(workId, args[0]);
            return Task.FromResult(true);
        });
        Assert.Equal(ServiceResultStatus.Success, (await service.ArchiveVehicleAsync(workId, userId, default)).Status);
    }

    /// <summary>Тестовый прокси, передающий вызовы настроенному обработчику.</summary>
    public class InvocationProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        /// <summary>Передаёт вызов зависимости настроенному тестовому обработчику.</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
