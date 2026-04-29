using Microsoft.EntityFrameworkCore;
using OficinaMvp.Api.Domain.Entities;

namespace OficinaMvp.Api.Infrastructure.Persistence;

public sealed class WorkshopDbContext : DbContext
{
    public WorkshopDbContext(DbContextOptions<WorkshopDbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<RepairService> RepairServices => Set<RepairService>();
    public DbSet<PartSupply> PartSupplies => Set<PartSupply>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers");
            entity.HasKey(customer => customer.Id);
            entity.Property(customer => customer.Name).IsRequired().HasMaxLength(150);
            entity.Property(customer => customer.Document).IsRequired().HasMaxLength(14);
            entity.Property(customer => customer.Phone).HasMaxLength(25);
            entity.Property(customer => customer.Email).HasMaxLength(150);
            entity.HasIndex(customer => customer.Document).IsUnique();
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("vehicles");
            entity.HasKey(vehicle => vehicle.Id);
            entity.Property(vehicle => vehicle.LicensePlate).IsRequired().HasMaxLength(7);
            entity.Property(vehicle => vehicle.Brand).IsRequired().HasMaxLength(100);
            entity.Property(vehicle => vehicle.Model).IsRequired().HasMaxLength(100);
            entity.HasIndex(vehicle => vehicle.LicensePlate).IsUnique();

            entity.HasOne(vehicle => vehicle.Customer)
                .WithMany(customer => customer.Vehicles)
                .HasForeignKey(vehicle => vehicle.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RepairService>(entity =>
        {
            entity.ToTable("repair_services");
            entity.HasKey(service => service.Id);
            entity.Property(service => service.Name).IsRequired().HasMaxLength(150);
            entity.Property(service => service.Description).IsRequired().HasMaxLength(300);
            entity.Property(service => service.LaborPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PartSupply>(entity =>
        {
            entity.ToTable("part_supplies");
            entity.HasKey(part => part.Id);
            entity.Property(part => part.Name).IsRequired().HasMaxLength(150);
            entity.Property(part => part.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.ToTable("work_orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(order => order.Notes).HasMaxLength(500);
            entity.Property(order => order.ServicesTotal).HasPrecision(18, 2);
            entity.Property(order => order.PartsTotal).HasPrecision(18, 2);
            entity.Property(order => order.BudgetTotal).HasPrecision(18, 2);

            entity.HasOne(order => order.Customer)
                .WithMany()
                .HasForeignKey(order => order.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(order => order.Vehicle)
                .WithMany()
                .HasForeignKey(order => order.VehicleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(order => order.Services)
                .WithOne()
                .HasForeignKey(line => line.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(order => order.Parts)
                .WithOne()
                .HasForeignKey(line => line.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(order => order.StatusHistory)
                .WithOne()
                .HasForeignKey(history => history.WorkOrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkOrderServiceLine>(entity =>
        {
            entity.ToTable("work_order_service_lines");
            entity.HasKey(line => line.Id);
            entity.Property(line => line.Id).ValueGeneratedOnAdd();
            entity.Property(line => line.ServiceName).IsRequired().HasMaxLength(150);
            entity.Property(line => line.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<WorkOrderPartLine>(entity =>
        {
            entity.ToTable("work_order_part_lines");
            entity.HasKey(line => line.Id);
            entity.Property(line => line.Id).ValueGeneratedOnAdd();
            entity.Property(line => line.PartName).IsRequired().HasMaxLength(150);
            entity.Property(line => line.UnitPrice).HasPrecision(18, 2);
        });

        modelBuilder.Entity<WorkOrderStatusHistory>(entity =>
        {
            entity.ToTable("work_order_status_history");
            entity.HasKey(history => history.Id);
            entity.Property(history => history.Id).ValueGeneratedOnAdd();
            entity.Property(history => history.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(history => history.Note).IsRequired().HasMaxLength(300);
        });
    }
}
