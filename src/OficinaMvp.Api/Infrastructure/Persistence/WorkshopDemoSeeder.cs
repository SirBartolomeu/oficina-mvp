using Microsoft.EntityFrameworkCore;
using OficinaMvp.Api.Domain.Entities;

namespace OficinaMvp.Api.Infrastructure.Persistence;

public static class WorkshopDemoSeeder
{
    public static async Task SeedAsync(WorkshopDbContext dbContext, ILogger logger, CancellationToken cancellationToken = default)
    {
        var changes = 0;
        const string demoDocument = "52998224725";
        const string demoPlate = "BRA2E19";

        var customer = await dbContext.Customers
            .FirstOrDefaultAsync(item => item.Document == demoDocument, cancellationToken);

        if (customer is null)
        {
            customer = new Customer(
                name: "Cliente Demo",
                document: demoDocument,
                phone: "(11) 99999-0000",
                email: "cliente.demo@oficina.local");

            dbContext.Customers.Add(customer);
            changes++;
        }

        var vehicle = await dbContext.Vehicles
            .FirstOrDefaultAsync(item => item.LicensePlate == demoPlate, cancellationToken);

        if (vehicle is null)
        {
            dbContext.Vehicles.Add(new Vehicle(
                customerId: customer.Id,
                licensePlate: demoPlate,
                brand: "Volkswagen",
                model: "Gol",
                year: 2021));

            changes++;
        }
        else if (vehicle.CustomerId != customer.Id)
        {
            logger.LogWarning("A placa demo {Plate} já existe vinculada a outro cliente. Seed de veículo ignorada.", demoPlate);
        }

        changes += await EnsureServiceAsync(dbContext, "Troca de óleo", "Troca de óleo e filtros", 150m, 60, cancellationToken);
        changes += await EnsureServiceAsync(dbContext, "Alinhamento", "Alinhamento e balanceamento", 120m, 45, cancellationToken);

        changes += await EnsurePartAsync(dbContext, "Filtro de óleo", 30m, 20, cancellationToken);
        changes += await EnsurePartAsync(dbContext, "Óleo 5W30 (1L)", 45m, 50, cancellationToken);

        if (changes == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seed de dados de demonstração aplicado. Registros incluídos: {Changes}.", changes);
    }

    private static async Task<int> EnsureServiceAsync(
        WorkshopDbContext dbContext,
        string name,
        string description,
        decimal laborPrice,
        int averageDurationMinutes,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.RepairServices
            .AnyAsync(item => item.Name == name, cancellationToken);

        if (exists)
        {
            return 0;
        }

        dbContext.RepairServices.Add(new RepairService(name, description, laborPrice, averageDurationMinutes));
        return 1;
    }

    private static async Task<int> EnsurePartAsync(
        WorkshopDbContext dbContext,
        string name,
        decimal unitPrice,
        int stockQuantity,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.PartSupplies
            .AnyAsync(item => item.Name == name, cancellationToken);

        if (exists)
        {
            return 0;
        }

        dbContext.PartSupplies.Add(new PartSupply(name, unitPrice, stockQuantity));
        return 1;
    }
}
