using Microsoft.EntityFrameworkCore;
using Oficina.Execution.Contracts;
using Oficina.Execution.Persistence;
using OficinaMvp.Domain.Exceptions;

namespace Oficina.Execution;

public sealed class ExecutionService(ExecutionDbContext db)
{
    public async Task<QuoteResponse> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken)
    {
        var services = Normalize(request.Services);
        var parts = Normalize(request.Parts);
        if (services.Count == 0 && parts.Count == 0)
            throw new DomainException("A cotação deve conter serviço ou peça.");

        var serviceIds = services.Keys.ToList();
        var partIds = parts.Keys.ToList();
        var knownServices = await db.RepairServices.AsNoTracking()
            .Where(item => serviceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var knownParts = await db.PartSupplies.AsNoTracking()
            .Where(item => partIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (knownServices.Count != services.Count || knownParts.Count != parts.Count)
            throw new DomainException("Serviço ou peça não encontrado.");

        var quotedServices = services.Select(item =>
            new QuotedItem(item.Key, knownServices[item.Key].Name, item.Value,
                ToCents(knownServices[item.Key].LaborPrice))).ToList();
        var quotedParts = parts.Select(item =>
            new QuotedItem(item.Key, knownParts[item.Key].Name, item.Value,
                ToCents(knownParts[item.Key].UnitPrice))).ToList();
        var total = quotedServices.Concat(quotedParts).Sum(item => checked(item.UnitPriceCents * item.Quantity));
        return new QuoteResponse(quotedServices, quotedParts, total);
    }

    public async Task<ReservationResponse> ReserveAsync(ReserveStockRequest request, CancellationToken cancellationToken)
    {
        if (request.ReservationId == Guid.Empty || request.WorkOrderId == Guid.Empty)
            throw new DomainException("Identificadores da reserva e OS são obrigatórios.");
        var quantities = Normalize(request.Parts);
        var existing = await db.StockReservations.Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == request.ReservationId, cancellationToken);
        if (existing is not null)
        {
            if (existing.WorkOrderId != request.WorkOrderId ||
                existing.Lines.Count != quantities.Count ||
                existing.Lines.Any(line => !quantities.TryGetValue(line.PartId, out var quantity) || quantity != line.Quantity))
                throw new DomainException("A chave de reserva já foi usada com outra solicitação.");
            return new ReservationResponse(existing.Id, existing.WorkOrderId, existing.Status.ToString());
        }

        var partIds = quantities.Keys.ToList();
        var parts = await db.PartSupplies.Where(item => partIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (parts.Count != quantities.Count)
            throw new DomainException("Peça não encontrada para reserva.");
        foreach (var item in quantities)
            parts[item.Key].RemoveFromStock(item.Value);

        db.StockReservations.Add(new StockReservation(request.ReservationId, request.WorkOrderId,
            quantities.Select(item => new StockReservationLine(item.Key, item.Value))));
        await db.SaveChangesAsync(cancellationToken);
        return new ReservationResponse(request.ReservationId, request.WorkOrderId, ReservationStatus.Reserved.ToString());
    }

    public async Task<ReservationResponse?> ReleaseAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await db.StockReservations.Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken);
        if (reservation is null) return null;
        if (reservation.Status == ReservationStatus.Released)
            return new ReservationResponse(reservation.Id, reservation.WorkOrderId, reservation.Status.ToString());
        if (reservation.Status == ReservationStatus.Consumed)
            throw new DomainException("Reserva consumida não pode ser liberada automaticamente.");

        var partIds = reservation.Lines.Select(item => item.PartId).ToList();
        var parts = await db.PartSupplies.Where(item => partIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        if (parts.Count != partIds.Count)
            throw new DomainException("Peça da reserva não encontrada.");
        foreach (var line in reservation.Lines)
            parts[line.PartId].ReturnToStock(line.Quantity);

        reservation.Release();
        await db.SaveChangesAsync(cancellationToken);
        return new ReservationResponse(reservation.Id, reservation.WorkOrderId, reservation.Status.ToString());
    }

    private static Dictionary<Guid, int> Normalize(IReadOnlyCollection<CatalogItemRequest> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(item => item.Id == Guid.Empty || item.Quantity <= 0))
            throw new DomainException("Cada item precisa de identificador e quantidade positiva.");
        return items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => checked(group.Sum(item => item.Quantity)));
    }

    private static long ToCents(decimal amount) => checked((long)(decimal.Round(amount, 2, MidpointRounding.AwayFromZero) * 100m));
}
