using Microsoft.EntityFrameworkCore;
using OficinaMvp.Application.Ports;
using OficinaMvp.Domain.Entities;

namespace OficinaMvp.Infrastructure.Persistence.Repositories;

public sealed class EfWorkshopRepository : IWorkshopRepository
{
    private readonly WorkshopDbContext _dbContext;

    public EfWorkshopRepository(WorkshopDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<Customer>> ListCustomersAsync(CancellationToken cancellationToken) =>
        await _dbContext.Customers
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

    public async Task<Customer?> GetCustomerByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = trackChanges ? _dbContext.Customers : _dbContext.Customers.AsNoTracking();
        return await query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public void AddCustomer(Customer customer) => _dbContext.Customers.Add(customer);
    public void RemoveCustomer(Customer customer) => _dbContext.Customers.Remove(customer);

    public async Task<IReadOnlyCollection<Vehicle>> ListVehiclesAsync(CancellationToken cancellationToken) =>
        await _dbContext.Vehicles
            .AsNoTracking()
            .OrderBy(item => item.Brand)
            .ThenBy(item => item.Model)
            .ToListAsync(cancellationToken);

    public async Task<Vehicle?> GetVehicleByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = trackChanges ? _dbContext.Vehicles : _dbContext.Vehicles.AsNoTracking();
        return await query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<Vehicle?> GetVehicleByLicensePlateAsync(string licensePlate, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = trackChanges ? _dbContext.Vehicles : _dbContext.Vehicles.AsNoTracking();
        return await query.FirstOrDefaultAsync(item => item.LicensePlate == licensePlate, cancellationToken);
    }

    public void AddVehicle(Vehicle vehicle) => _dbContext.Vehicles.Add(vehicle);
    public void RemoveVehicle(Vehicle vehicle) => _dbContext.Vehicles.Remove(vehicle);

    public async Task<IReadOnlyCollection<RepairService>> ListRepairServicesAsync(CancellationToken cancellationToken) =>
        await _dbContext.RepairServices
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

    public async Task<RepairService?> GetRepairServiceByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = trackChanges ? _dbContext.RepairServices : _dbContext.RepairServices.AsNoTracking();
        return await query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, RepairService>> GetRepairServicesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await _dbContext.RepairServices
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

    public void AddRepairService(RepairService service) => _dbContext.RepairServices.Add(service);
    public void RemoveRepairService(RepairService service) => _dbContext.RepairServices.Remove(service);

    public async Task<IReadOnlyCollection<PartSupply>> ListPartSuppliesAsync(CancellationToken cancellationToken) =>
        await _dbContext.PartSupplies
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

    public async Task<PartSupply?> GetPartSupplyByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = trackChanges ? _dbContext.PartSupplies : _dbContext.PartSupplies.AsNoTracking();
        return await query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, PartSupply>> GetPartSuppliesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await _dbContext.PartSupplies
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

    public void AddPartSupply(PartSupply partSupply) => _dbContext.PartSupplies.Add(partSupply);
    public void RemovePartSupply(PartSupply partSupply) => _dbContext.PartSupplies.Remove(partSupply);

    public async Task<WorkOrder?> GetWorkOrderByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken)
    {
        var query = WorkOrderQuery();
        if (!trackChanges)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<WorkOrder>> ListWorkOrdersAsync(CancellationToken cancellationToken) =>
        await WorkOrderQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<WorkOrder>> ListFinishedWorkOrdersAsync(CancellationToken cancellationToken) =>
        await _dbContext.WorkOrders
            .AsNoTracking()
            .Where(item => item.FinalizedAtUtc.HasValue && item.ExecutionStartedAtUtc.HasValue)
            .ToListAsync(cancellationToken);

    public void AddWorkOrder(WorkOrder workOrder) => _dbContext.WorkOrders.Add(workOrder);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<WorkOrder> WorkOrderQuery() =>
        _dbContext.WorkOrders
            .Include(item => item.Services)
            .Include(item => item.Parts)
            .Include(item => item.StatusHistory);
}
