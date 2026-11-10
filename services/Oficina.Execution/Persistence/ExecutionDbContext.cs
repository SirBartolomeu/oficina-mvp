using Microsoft.EntityFrameworkCore;
using OficinaMvp.Domain.Entities;

namespace Oficina.Execution.Persistence;

public sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options)
{
    public DbSet<RepairService> RepairServices => Set<RepairService>();
    public DbSet<PartSupply> PartSupplies => Set<PartSupply>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
    public List<StockReservationLine> Lines { get; private set; } = [];
    public void Release() => Status = ReservationStatus.Released;
    public void Consume() => Status = ReservationStatus.Consumed;
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
