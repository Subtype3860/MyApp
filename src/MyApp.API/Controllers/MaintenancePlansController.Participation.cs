using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db.Entities;
using System.Text.Json;

namespace MyApp.API.Controllers;

public sealed partial class MaintenancePlansController
{
    /// <summary>Запрос на участие в разделе карты ТО.</summary>
    public sealed record CardParticipationRequest(string Section, string? UserId = null, bool? Approve = null);

    /// <summary>Отмечает текущего пользователя участником раздела карты ТО.</summary>
    [HttpPost("cards/{id:guid}/participation")]
    public async Task<IActionResult> AddCardParticipation(Guid id, [FromBody] CardParticipationRequest request, CancellationToken ct)
    {
        if (!await CanFill(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Section)) return BadRequest(new { message = "Укажите раздел карты." });
        var card = await db.MaintenanceCards.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (card is null) return NotFound();
        if (card.Status != "draft") return BadRequest(new { message = "Участие отмечается в черновике карты." });
        var actor = Actor;
        var employee = await db.Users.AsNoTracking().Where(x => x.Id == actor)
            .Select(x => new
            {
                x.FirstName,
                x.LastName,
                x.MiddleName,
                Position = x.Profession != null ? x.Profession.Name : ""
            }).SingleOrDefaultAsync(ct);
        var name = employee is null ? "" : string.Join(" ", new[] { employee.LastName, employee.FirstName, employee.MiddleName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var sections = UpdateSections(card, current =>
        {
            var section = current.FirstOrDefault(item => item.Name == request.Section)
                ?? new CardSection(request.Section, []);
            var participants = section.Participants.ToList();
            if (!participants.Any(item => item.UserId == actor.ToString()))
                participants.Add(new CardParticipant(actor.ToString(), name, employee?.Position ?? "", "pending"));
            return current.Where(item => item.Name != request.Section)
                .Append(new CardSection(request.Section, participants.ToArray())).ToArray();
        });
        AuditCard(card, "participation", request.Section);
        await db.SaveChangesAsync(ct);
        return Ok(new { sections });
    }

    /// <summary>Подтверждает или отклоняет участие работника в разделе (только механик).</summary>
    [HttpPost("cards/{id:guid}/participation/decision")]
    public async Task<IActionResult> DecideCardParticipation(Guid id, [FromBody] CardParticipationRequest request, CancellationToken ct)
    {
        if (!await CanManage(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Section) || string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { message = "Укажите раздел и работника." });
        var card = await db.MaintenanceCards.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (card is null) return NotFound();
        if (card.Status != "draft") return BadRequest(new { message = "Участие подтверждается в черновике карты." });
        var status = request.Approve == true ? "approved" : "rejected";
        var approver = await db.Users.AsNoTracking().Where(x => x.Id == Actor)
            .Select(x => new { x.FirstName, x.LastName, x.MiddleName }).SingleOrDefaultAsync(ct);
        var approverName = approver is null ? "" : string.Join(" ", new[] { approver.LastName, approver.FirstName, approver.MiddleName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var found = false;
        var sections = UpdateSections(card, current => current.Select(section =>
        {
            if (section.Name != request.Section) return section;
            var participants = section.Participants.Select(person =>
            {
                if (person.UserId != request.UserId) return person;
                found = true;
                return person with { Status = status, ApprovedBy = approverName, ApprovedAt = DateTimeOffset.UtcNow };
            }).ToArray();
            return new CardSection(section.Name, participants);
        }).ToArray());
        if (!found) return NotFound();
        AuditCard(card, status == "approved" ? "participation-approved" : "participation-rejected", request.Section);
        await db.SaveChangesAsync(ct);
        return Ok(new { sections });
    }

    /// <summary>Применяет изменение разделов к данным карты и возвращает новое состояние.</summary>
    private CardSection[] UpdateSections(MaintenanceCard card, Func<CardSection[], CardSection[]> change)
    {
        var data = Decode<CardData>(card.Data);
        var sections = change(data.Sections ?? []);
        card.Data = JsonSerializer.Serialize(data with { Sections = sections }, JsonOptions);
        card.Revision++;
        return sections;
    }
}
