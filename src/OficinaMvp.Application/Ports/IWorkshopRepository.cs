using OficinaMvp.Domain.Entities;

namespace OficinaMvp.Application.Ports;

public interface IWorkshopRepository
{
    Task<IReadOnlyCollection<Customer>> ListCustomersAsync(CancellationToken cancellationToken);
    Task<Customer?> GetCustomerByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    void AddCustomer(Customer customer);
    void RemoveCustomer(Customer customer);

    Task<IReadOnlyCollection<Vehicle>> ListVehiclesAsync(CancellationToken cancellationToken);
    Task<Vehicle?> GetVehicleByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    Task<Vehicle?> GetVehicleByLicensePlateAsync(string licensePlate, bool trackChanges, CancellationToken cancellationToken);
    void AddVehicle(Vehicle vehicle);
    void RemoveVehicle(Vehicle vehicle);

    Task<IReadOnlyCollection<RepairService>> ListRepairServicesAsync(CancellationToken cancellationToken);
    Task<RepairService?> GetRepairServiceByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, RepairService>> GetRepairServicesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    void AddRepairService(RepairService service);
    void RemoveRepairService(RepairService service);

    Task<IReadOnlyCollection<PartSupply>> ListPartSuppliesAsync(CancellationToken cancellationToken);
    Task<PartSupply?> GetPartSupplyByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, PartSupply>> GetPartSuppliesByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    void AddPartSupply(PartSupply partSupply);
    void RemovePartSupply(PartSupply partSupply);

    Task<WorkOrder?> GetWorkOrderByIdAsync(Guid id, bool trackChanges, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<WorkOrder>> ListWorkOrdersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<WorkOrder>> ListFinishedWorkOrdersAsync(CancellationToken cancellationToken);
    void AddWorkOrder(WorkOrder workOrder);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
