using Microsoft.EntityFrameworkCore;
using OficinaMvp.Domain.Entities;

namespace Oficina.Execution.Persistence;

public sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options)
{
    public DbSet<RepairService> RepairServices => Set<RepairService>();
    public DbSet<PartSupply> PartSupplies => Set<PartSupply>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<ExecutionInboxMessage> InboxMessages => Set<ExecutionInboxMessage>();
    public DbSet<ExecutionOutboxMessage> OutboxMessages => Set<ExecutionOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExecutionInboxMessage>(entity =>
        {
            entity.ToTable("inbox_messages");
            entity.HasKey(item => item.EventId);
            entity.Property(item => item.EventType).IsRequired().HasMaxLength(80);
        });
        modelBuilder.Entity<ExecutionOutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(item => item.EventId);
            entity.Property(item => item.EventType).IsRequired().HasMaxLength(80);
            entity.Property(item => item.Body).IsRequired();
            entity.Property(item => item.LastError).HasMaxLength(500);
            entity.HasIndex(item => new { item.PublishedAtUtc, item.OccurredAtUtc });
        });

        modelBuilder.Entity<RepairService>(entity =>
        {
            entity.ToTable("repair_services");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).IsRequired().HasMaxLength(150);
            entity.Property(item => item.Description).IsRequired().HasMaxLength(300);
            entity.Property(item => item.LaborPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PartSupply>(entity =>
        {
            entity.ToTable("part_supplies");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).IsRequired().HasMaxLength(150);
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2);
            entity.Property(item => item.StockQuantity).IsConcurrencyToken();
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("stock_reservations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(item => item.RepairStatus).HasConversion<string>()
                .HasMaxLength(24).IsConcurrencyToken();
            entity.Property(item => item.BranchId).HasMaxLength(64);
            entity.Property(item => item.RejectionReason).HasMaxLength(180);
            entity.HasIndex(item => item.WorkOrderId);
            entity.HasMany(item => item.Lines).WithOne().HasForeignKey(item => item.ReservationId);
        });

        modelBuilder.Entity<StockReservationLine>(entity =>
        {
            entity.ToTable("stock_reservation_lines");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => new { item.ReservationId, item.PartId }).IsUnique();
        });
    }
}

public enum ReservationStatus { Reserved = 1, Released = 2, Consumed = 3 }
public enum RepairStatus { Started = 1, Finished = 2, Rejected = 3 }

public sealed class StockReservation
{
    private StockReservation() { }
    public StockReservation(Guid id, Guid workOrderId, IEnumerable<StockReservationLine> lines)
    {
        Id = id;
        WorkOrderId = workOrderId;
        Lines = lines.ToList();
        Status = ReservationStatus.Reserved;
    }

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public RepairStatus? RepairStatus { get; private set; }
    public Guid? PaymentAttemptId { get; private set; }
    public string? BranchId { get; private set; }
    public DateTime? RepairStartedAtUtc { get; private set; }
    public DateTime? RepairFinishedAtUtc { get; private set; }
    public DateTime? RepairRejectedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public List<StockReservationLine> Lines { get; private set; } = [];
    public void Release() => Status = ReservationStatus.Released;
    public void Consume() => Status = ReservationStatus.Consumed;
    public void StartRepair(Guid paymentAttemptId, string? branchId, DateTime atUtc)
    {
        if (Status != ReservationStatus.Consumed || paymentAttemptId == Guid.Empty)
            throw new OficinaMvp.Domain.Exceptions.DomainException(
                "Reparo exige pagamento identificado e reserva consumida.");
        if (RepairStatus.HasValue)
        {
            if (PaymentAttemptId == paymentAttemptId) return;
            throw new OficinaMvp.Domain.Exceptions.DomainException(
                "Reserva já vinculada a outro pagamento.");
        }
        PaymentAttemptId = paymentAttemptId;
        BranchId = branchId;
        RepairStartedAtUtc = atUtc;
        RepairStatus = Oficina.Execution.Persistence.RepairStatus.Started;
    }
    public bool FinishRepair(DateTime atUtc)
    {
        if (RepairStatus == Oficina.Execution.Persistence.RepairStatus.Finished) return false;
        if (RepairStatus != Oficina.Execution.Persistence.RepairStatus.Started)
            throw new OficinaMvp.Domain.Exceptions.DomainException("Reparo ainda não iniciado.");
        RepairFinishedAtUtc = atUtc;
        RepairStatus = Oficina.Execution.Persistence.RepairStatus.Finished;
        return true;
    }
    public bool RejectRepair(string reason, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 180)
            throw new OficinaMvp.Domain.Exceptions.DomainException("Motivo da recusa inválido.");
        if (RepairStatus == Oficina.Execution.Persistence.RepairStatus.Rejected)
        {
            if (RejectionReason == reason.Trim()) return false;
            throw new OficinaMvp.Domain.Exceptions.DomainException(
                "Reparo já recusado com outro motivo.");
        }
        if (RepairStatus != Oficina.Execution.Persistence.RepairStatus.Started)
            throw new OficinaMvp.Domain.Exceptions.DomainException(
                "Somente um reparo iniciado pode ser recusado.");
        RejectionReason = reason.Trim();
        RepairRejectedAtUtc = atUtc;
        RepairStatus = Oficina.Execution.Persistence.RepairStatus.Rejected;
        return true;
    }
}

public sealed class StockReservationLine
{
    private StockReservationLine() { }
    public StockReservationLine(Guid partId, int quantity)
    {
        Id = Guid.NewGuid();
        PartId = partId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid ReservationId { get; private set; }
    public Guid PartId { get; private set; }
    public int Quantity { get; private set; }
}
