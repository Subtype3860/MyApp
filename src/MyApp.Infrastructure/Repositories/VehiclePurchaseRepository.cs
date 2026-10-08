using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Application.DTO;
using MyApp.Infrastructure.Db;
using MyApp.Infrastructure.Db.Entities;

namespace MyApp.Infrastructure.Repositories;

public sealed class VehiclePurchaseRepository(AppDbContext db) : IVehiclePurchaseRepository
{
    public async Task<Guid> AddPurchaseAsync(
        Guid vehicleId,
        VehiclePurchaseRequest request,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        var entity = new VehiclePurchaseEntity
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            RequestDate = request.RequestDate,
            RequestNumber = request.RequestNumber,
            ItemName = request.ItemName,
            Quantity = request.Quantity,
            Status = request.Status,
            Note = request.Note,
            CreatedBy = createdBy
        };

        db.VehiclePurchases.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task<IReadOnlyList<VehiclePurchaseResponse>> GetPurchasesAsync(
        Guid vehicleId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var query = db.VehiclePurchases.AsNoTracking()
            .Where(purchase => purchase.VehicleId == vehicleId);
        if (from.HasValue)
        {
            query = query.Where(purchase => purchase.RequestDate >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(purchase => purchase.RequestDate <= to.Value);
        }

        return await query
            .OrderByDescending(purchase => purchase.RequestDate)
            .ThenByDescending(purchase => purchase.CreatedAt)
            .Select(purchase => new VehiclePurchaseResponse(
                purchase.Id,
                purchase.RequestDate,
                purchase.RequestNumber,
                purchase.ItemName,
                purchase.Quantity,
                purchase.Status,
                purchase.Note,
                purchase.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
