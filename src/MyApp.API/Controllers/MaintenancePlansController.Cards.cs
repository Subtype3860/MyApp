using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.DTO;
using MyApp.Application.Services;
using MyApp.Application.Common;
using MyApp.Infrastructure.Db.Entities;
using MyApp.Infrastructure.Repositories;

namespace MyApp.API.Controllers;

public sealed partial class MaintenancePlansController
{
    [HttpGet("cards/{id:guid}/requirement-preview")]
    public async Task<IActionResult> CardRequirement(Guid id, CancellationToken ct)
    {
        if (!await CanFill(ct)) return Forbid();
        var card = await db.MaintenanceCards.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (card is null) return NotFound();
        var template = Decode<CardTemplate>(card.Template);
        var wanted = template.Materials.Where(m => m.Quantity > 0).GroupBy(m => new { Name = m.Name.Trim(), m.Unit })
            .Select(g => new { g.Key.Name, g.Key.Unit, Quantity = g.Sum(m => m.Quantity!.Value) }).ToArray();
        var names = wanted.Select(m => m.Name).ToArray();
        var stocks = await db.FullStocks.AsNoTracking().Where(x => names.Contains(x.Name.Trim())).ToArrayAsync(ct);
        return Ok(wanted.Select(m => {
            var matches = stocks.Where(s => s.Name.Trim() == m.Name && s.Unit.Trim() == m.Unit.Trim()).ToArray();
            decimal available = 0;
            var matched = matches.Length == 1 && decimal.TryParse(matches[0].Quantity?.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out available);
            return new { materialName = m.Name, unit = m.Unit, quantity = m.Quantity, availableQuantity = matched ? Math.Max(available,0) : 0, matched };
        }));
    }
    public sealed record TemplateWrite(int Revision, bool Published, CardTemplate Template);
    public sealed record CardStart(Guid Id, Guid TemplateId, int TemplateRevision, int PlanRevision, Guid? RunInId = null);
    public sealed record CardWrite(int Revision, CardData Data, string Action = "save", string Reason = "");
    public sealed record CardDecision(int Revision, string Reason = "");
    private static T Decode<T>(string text) => JsonSerializer.Deserialize<T>(text, JsonOptions)!;
    private async Task<bool> CanUseLibrary(CancellationToken ct) => Actor != Guid.Empty &&
        MyApp.Application.Security.MaintenanceLibraryAccess.Allows(await db.Users.AsNoTracking()
            .Where(x => x.Id == Actor).Select(x => x.Profession.Name).SingleOrDefaultAsync(ct));
    private async Task<bool> CanFill(CancellationToken ct) => Actor != Guid.Empty &&
        (User.HasClaim("permission", "vehicles.maintenance_execution") || await CanManage(ct));
    private object CardLite(MaintenanceCard card) => new { card.Id, card.VehicleId, card.VehicleLabel, card.Revision, card.Status,
        card.TemplateId, card.TemplateRevision, card.PlanRevision, card.RunInId, card.CreatedBy, card.CreatedAt, card.AcceptedBy, card.AcceptedAt,
        auditCount = Decode<CardAudit[]>(card.Audit).Length,
        template = Decode<CardTemplate>(card.Template), data = Decode<CardData>(card.Data),
        defects = Decode<Dictionary<string,Guid>>(card.Defects), readings = Decode<MaintenanceEngine[]>(card.Readings) };
    private object CardView(MaintenanceCard card) => new { card.Id, card.VehicleId, card.VehicleLabel, card.Revision, card.Status,
        card.TemplateId, card.TemplateRevision, card.PlanRevision, card.RunInId, card.CreatedBy, card.CreatedAt, card.AcceptedBy, card.AcceptedAt,
        template = Decode<CardTemplate>(card.Template), data = Decode<CardData>(card.Data), audit = Decode<CardAudit[]>(card.Audit),
        defects = Decode<Dictionary<string,Guid>>(card.Defects), readings = Decode<MaintenanceEngine[]>(card.Readings) };
    private void AuditCard(MaintenanceCard card, string action, string reason = "")
    {
        card.Revision++;
        var audit = Decode<List<CardAudit>>(card.Audit);
        audit.Add(new(card.Revision, Actor, DateTimeOffset.UtcNow, action, reason, Decode<CardData>(card.Data)));
        // Храним только последние проверки: старые снимки раздувают ответы карты.
        if (audit.Count > 40) audit.RemoveRange(0, audit.Count - 40);
        card.Audit = JsonSerializer.Serialize(audit, JsonOptions);
    }
    private IActionResult CardConflict() => Conflict(new { message = "Карта изменена другим сотрудником. Сохранённый локальный черновик доступен; загрузите актуальную карту и сравните изменения." });

    [HttpGet("card-template-options")]
    public async Task<IActionResult> CardTemplateOptions(CancellationToken ct) => Ok(new {
        canManage = await CanManage(ct), canFill = await CanFill(ct), canUseLibrary = await CanUseLibrary(ct),
        templates = (await db.MaintenanceCardTemplates.AsNoTracking().Where(t => t.Published).ToArrayAsync(ct))
            .Select(t => { var template = Decode<CardTemplate>(t.Snapshot); return new {
                t.Id, t.Revision, t.Published, template = new { template.Model, template.Service, template.Source } }; })
    });

    [HttpGet("card-templates")]
    public async Task<IActionResult> CardTemplates(CancellationToken ct)
    {
        if (!await CanUseLibrary(ct)) return Forbid();
        return Ok(new {
        materialSources = await new MaintenanceTemplateRepository(db).GetAllAsync(ct),
        templates = (await db.MaintenanceCardTemplates.AsNoTracking().ToArrayAsync(ct)).Select(t => new {
            t.Id, t.Revision, t.Published, t.ApprovedBy, t.ApprovedAt, template = Decode<CardTemplate>(t.Snapshot) }) });
    }

    [HttpPut("card-templates/{id:guid}")]
    [RequestSizeLimit(500_000)]
    public async Task<IActionResult> SaveCardTemplate(Guid id, TemplateWrite request, CancellationToken ct)
    {
        if (!await CanUseLibrary(ct)) return Forbid();
        if (id == Guid.Empty || request.Revision < 0) return BadRequest();
        // Materials are maintained in Technical Maintenance, never duplicated in card definitions.
        if (request.Template is not null) request = request with { Template = request.Template with { Materials = [], MaterialSource = null } };
        var error = MaintenanceCardRules.ValidateTemplate(request.Template);
        // Unresolved imported units can be repaired while the template is still a draft.
        if (error is not null) return BadRequest(new { message = error });
        if (request.Template!.MaterialIntervalId is { } sourceId)
        {
            var interval = await db.MaintenanceIntervals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, ct);
            if (interval is null || MaintenanceCardRules.Normalize(interval.Name) != MaintenanceCardRules.Normalize(request.Template.Service))
                return BadRequest(new { message = "Источник материалов должен соответствовать виду ТО карты." });
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 721))", ct);
        var item = await db.MaintenanceCardTemplates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if ((item?.Revision ?? 0) != request.Revision) return CardConflict();
        item ??= new() { Id = id };
        if (item.Revision == 0) db.MaintenanceCardTemplates.Add(item);
        item.Revision++; item.Published = request.Published; item.Snapshot = JsonSerializer.Serialize(request.Template, JsonOptions);
        item.ApprovedBy = request.Published ? Actor : null; item.ApprovedAt = request.Published ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Ok(new { item.Revision });
    }

    [HttpGet("{vehicleId:guid}/cards")]
    public async Task<IActionResult> Cards(Guid vehicleId, CancellationToken ct)
    {
        if (!(await vehicles.GetVehiclesAsync(ct)).Any(x => x.Id == vehicleId)) return NotFound();
        return Ok((await db.MaintenanceCards.AsNoTracking().Where(x => x.VehicleId == vehicleId).OrderByDescending(x => x.CreatedAt).ToArrayAsync(ct)).Select(CardLite));
    }

    [HttpPost("{vehicleId:guid}/cards")]
    public async Task<IActionResult> StartCard(Guid vehicleId, CardStart request, CancellationToken ct)
    {
        if (!await CanFill(ct)) return Forbid();
        if (request.Id == Guid.Empty) return BadRequest();
        var vehicle = (await vehicles.GetVehiclesAsync(ct)).FirstOrDefault(x => x.Id == vehicleId && !x.IsArchived);
        if (vehicle is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(vehicleId, ct);
        var repeat = await db.MaintenanceCards.SingleOrDefaultAsync(x => x.Id == request.Id, ct);
        if (repeat is not null) return repeat.VehicleId == vehicleId && repeat.CreatedBy == Actor && repeat.TemplateId == request.TemplateId && repeat.PlanRevision == request.PlanRevision && repeat.RunInId == request.RunInId && repeat.TemplateRevision == request.TemplateRevision
            ? Ok(CardLite(repeat)) : CardConflict();
        var plan = await db.VehicleMaintenancePlans.SingleOrDefaultAsync(x => x.VehicleId == vehicleId, ct);
        if (plan is null || plan.Revision != request.PlanRevision) return VersionConflict();
        var definition = Read(plan);
        if (request.RunInId is { } runIn && !definition.RunIns.Any(x => x.Id == runIn)) return BadRequest();
        var template = await db.MaintenanceCardTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TemplateId && x.Published, ct);
        if (template is null || template.Revision != request.TemplateRevision) return Conflict(new { message = "Шаблон не утверждён или изменён. Обновите библиотеку." });
        var content = Decode<CardTemplate>(template.Snapshot);
        var expected = request.RunInId is null ? definition.Steps[definition.NextStepIndex].Name
            : "ТО-" + definition.RunIns.Single(r => r.Id == request.RunInId).Interval.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (MaintenanceCardRules.Normalize(content.Model) != MaintenanceCardRules.Normalize(vehicle.ModelName) || MaintenanceCardRules.Normalize(content.Service) != MaintenanceCardRules.Normalize(expected))
            return BadRequest(new { message = "Модель и вид ТО шаблона должны соответствовать машине и текущему шагу графика." });
        if (await db.MaintenanceCards.AnyAsync(x => x.VehicleId == vehicleId && x.RunInId == request.RunInId && (x.Status == "draft" || x.Status == "submitted"), ct))
            return Conflict(new { message = "Для этого ТО уже есть открытая карта. Продолжите её заполнение." });
        var resolved = MaintenanceCardMaterials.Resolve(content, vehicle.ModelName, vehicle.TypeName, await new MaintenanceTemplateRepository(db).GetAllAsync(ct));
        if (resolved.Error is not null) return BadRequest(new { message = resolved.Error });
        content = resolved.Template!;
        var data = new CardData(Today, "", "", null, null, DateTimeOffset.UtcNow, null, null, "",
            content.Operations.Select(o => new CardAnswer(o.Id,"","","","","")).ToArray(),
            content.Materials.Select(m => new CardConsumption(m.Id,null,"","")).ToArray());
        var card = new MaintenanceCard { Id = request.Id, VehicleId = vehicleId, VehicleLabel = $"{vehicle.ModelName} № {vehicle.GarageNumber} {vehicle.StateNumber} VIN {vehicle.Vin}",
            TemplateId = template.Id, TemplateRevision = template.Revision, PlanRevision = plan.Revision, RunInId = request.RunInId,
            Template = JsonSerializer.Serialize(content, JsonOptions), Data = JsonSerializer.Serialize(data, JsonOptions), CreatedBy = Actor, CreatedAt = DateTimeOffset.UtcNow };
        AuditCard(card, "created"); db.MaintenanceCards.Add(card);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(CardLite(card));
    }

    [HttpPost("cards/{id:guid}/refresh-materials")]
    public async Task<IActionResult> RefreshCardMaterials(Guid id, CardDecision request, CancellationToken ct)
    {
        if (!await CanFill(ct)) return Forbid();
        var card = await db.MaintenanceCards.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (card is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(card.VehicleId, ct); await db.Entry(card).ReloadAsync(ct);
        if (card.Revision != request.Revision) return CardConflict();
        if (card.Status != "draft") return BadRequest(new { message = "Материалы можно загрузить только в черновик." });
        var data = Decode<CardData>(card.Data);
        if (data.Materials.Any(m => m.Actual is not null || !string.IsNullOrWhiteSpace(m.Substitute)))
            return Conflict(new { message = "Фактический расход или аналоги уже заполнены. Список материалов сохранён без изменений." });
        var vehicle = (await vehicles.GetVehiclesAsync(ct)).FirstOrDefault(v => v.Id == card.VehicleId);
        if (vehicle is null) return NotFound();
        var content = Decode<CardTemplate>(card.Template);
        // An older draft may predate the material-source link. Use the latest explicit mapping if present.
        if (content.MaterialIntervalId is null)
        {
            var definition = await db.MaintenanceCardTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == card.TemplateId, ct);
            if (definition is not null) content = content with { MaterialIntervalId = Decode<CardTemplate>(definition.Snapshot).MaterialIntervalId };
        }
        var resolved = MaintenanceCardMaterials.Resolve(content, vehicle.ModelName, vehicle.TypeName, await new MaintenanceTemplateRepository(db).GetAllAsync(ct));
        if (resolved.Error is not null) return BadRequest(new { message = resolved.Error });
        content = resolved.Template!;
        var old = data.Materials.ToDictionary(m => m.Id);
        data = data with { Materials = content.Materials.Select(m => old.GetValueOrDefault(m.Id) ?? new CardConsumption(m.Id,null,"","")).ToArray() };
        card.Template = JsonSerializer.Serialize(content, JsonOptions); card.Data = JsonSerializer.Serialize(data, JsonOptions);
        AuditCard(card, "materials-refreshed", content.MaterialSource ?? "");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(CardLite(card));
    }

    [HttpPut("cards/{id:guid}")]
    [RequestSizeLimit(500_000)]
    public async Task<IActionResult> SaveCard(Guid id, CardWrite request, CancellationToken ct)
    {
        if (!await CanFill(ct)) return Forbid();
        var card = await db.MaintenanceCards.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (card is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(card.VehicleId, ct);
        await db.Entry(card).ReloadAsync(ct);
        if (card.Revision != request.Revision) return CardConflict();
        if (card.Status != "draft" || request.Action is not ("save" or "submit" or "cancel")) return BadRequest(new { message = "Можно редактировать только черновик." });
        if (request.Action == "cancel" && string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { message = "Укажите причину отмены." });
        var error = MaintenanceCardRules.Validate(Decode<CardTemplate>(card.Template), request.Data, Today, request.Action == "submit");
        if (error is not null) return BadRequest(new { message = error });
        card.Data = JsonSerializer.Serialize(request.Data, JsonOptions);
        if (request.Action == "submit") card.Status = "submitted";
        if (request.Action == "cancel") card.Status = "cancelled";
        AuditCard(card, request.Action, request.Reason);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(CardLite(card));
    }

    [HttpPost("cards/{id:guid}/return")]
    public async Task<IActionResult> ReturnCard(Guid id, CardDecision request, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 3000) return BadRequest(new { message = "Укажите причину возврата." });
        var card = await db.MaintenanceCards.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (card is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(card.VehicleId, ct); await db.Entry(card).ReloadAsync(ct);
        if (card.Revision != request.Revision) return CardConflict();
        if (card.Status != "submitted") return BadRequest();
        card.Status = "draft"; AuditCard(card, "returned", request.Reason);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(CardLite(card));
    }

    [HttpPost("cards/{id:guid}/accept")]
    public async Task<IActionResult> AcceptCard(Guid id, CardDecision request, CancellationToken ct)
    {
        var card = await db.MaintenanceCards.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (card is null) return NotFound();
        var data = Decode<CardData>(card.Data);
        return await Complete(card.VehicleId, new(card.PlanRevision, data.Date, [],
            $"Карта ТО {id}. {request.Reason}".Trim(), card.RunInId, id, id, request.Revision), ct);
    }

    [HttpPost("cards/{id:guid}/defects/{operationId}")]
    public async Task<IActionResult> CardDefect(Guid id, string operationId, CancellationToken ct)
    {
        if (!await CanFill(ct)) return Forbid();
        var card = await db.MaintenanceCards.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (card is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(card.VehicleId, ct); await db.Entry(card).ReloadAsync(ct);
        var links = Decode<Dictionary<string,Guid>>(card.Defects);
        if (links.TryGetValue(operationId,out var linked)) return Ok(new { id = linked });
        var answer = Decode<CardData>(card.Data).Answers.FirstOrDefault(x => x.Id == operationId);
        if (card.Status != "draft" || answer?.Condition != "defect" || string.IsNullOrWhiteSpace(answer.Note)) return BadRequest(new { message = "Сначала сохраните описание дефекта в черновике." });
        var result = await vehicles.AddDefectAsync(card.VehicleId, new(null, $"Карта ТО {id}: {answer.Note}", null, "operating",
            Decode<CardTemplate>(card.Template).Operations.Single(x => x.Id == operationId).Section), Actor, ct);
        if (result.Status != ServiceResultStatus.Success) return BadRequest(new { message = result.Message ?? "Не удалось создать дефект. Проверьте права и данные." });
        links[operationId] = result.Value; card.Defects = JsonSerializer.Serialize(links, JsonOptions); AuditCard(card, "defect", operationId);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(new { id = result.Value });
    }

    [HttpGet("cards/{id:guid}")]
    public async Task<IActionResult> Card(Guid id, CancellationToken ct)
    {
        if (!await CanFill(ct)) return Forbid();
        var card = await db.MaintenanceCards.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return card is null ? NotFound() : Ok(CardView(card));
    }

    [HttpGet("cards/{id:guid}/photos")]
    public async Task<IActionResult> CardPhotos(Guid id, CancellationToken ct)
    {
        if (!await db.MaintenanceCards.AnyAsync(x => x.Id == id, ct)) return NotFound();
        return Ok(await db.MaintenanceCardPhotos.Where(x => x.CardId == id).Select(x => new { x.Id, x.OperationId, x.ContentType, x.CreatedBy, x.CreatedAt }).ToArrayAsync(ct));
    }
    [HttpGet("cards/{id:guid}/photos/{photoId:guid}")]
    public async Task<IActionResult> CardPhoto(Guid id, Guid photoId, CancellationToken ct)
    {
        if (!await db.MaintenanceCards.AnyAsync(x => x.Id == id, ct)) return NotFound();
        var photo = await db.MaintenanceCardPhotos.AsNoTracking().SingleOrDefaultAsync(x => x.CardId == id && x.Id == photoId, ct);
        return photo is null ? NotFound() : File(photo.Content, photo.ContentType, enableRangeProcessing: true);
    }
    [HttpPost("cards/{id:guid}/photos/{photoId:guid}")]
    [RequestSizeLimit(26_000_000)]
    public async Task<IActionResult> AddCardPhoto(Guid id, Guid photoId, [FromForm] string operationId, IFormFile file, CancellationToken ct)
    {
        if (!await CanFill(ct)) return Forbid();
        if (photoId == Guid.Empty || file.Length is < 8 or > MaintenanceCardMedia.VideoLimit) return BadRequest(new { message = "Фото JPEG/PNG/WebP до 4 МБ, видео MP4/WebM до 25 МБ." });
        using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct); var bytes = buffer.ToArray();
        var type = MaintenanceCardMedia.ContentType(bytes);
        if (type is null) return BadRequest(new { message = "Допустимы фото JPEG/PNG/WebP до 4 МБ и видео MP4/WebM до 25 МБ." });
        var card = await db.MaintenanceCards.SingleOrDefaultAsync(x => x.Id == id, ct); if (card is null) return NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(card.VehicleId, ct); await db.Entry(card).ReloadAsync(ct);
        var old = await db.MaintenanceCardPhotos.SingleOrDefaultAsync(x => x.Id == photoId, ct);
        if (old is not null) return old.CardId == id && old.CreatedBy == Actor && old.OperationId == operationId && old.Content.SequenceEqual(bytes) ? Ok(new { id = photoId }) : CardConflict();
        if (card.Status != "draft" || !Decode<CardTemplate>(card.Template).Operations.Any(x => x.Id == operationId)) return BadRequest();
        if (await db.MaintenanceCardPhotos.CountAsync(x => x.CardId == id, ct) >= 20) return BadRequest(new { message = "Не более 20 медиафайлов на карту." });
        db.MaintenanceCardPhotos.Add(new() { Id = photoId, CardId = id, OperationId = operationId, Content = bytes, ContentType = type, CreatedBy = Actor, CreatedAt = DateTimeOffset.UtcNow });
        AuditCard(card,"photo",operationId); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(new { id = photoId });
    }
}
