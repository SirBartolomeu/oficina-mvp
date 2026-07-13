using OficinaMvp.Application.Ports;
using OficinaMvp.Domain.Entities;
using OficinaMvp.Domain.Exceptions;

namespace OficinaMvp.Application.Services;

public sealed class WorkshopCatalogApplicationService
{
    private readonly IWorkshopRepository _repository;

    public WorkshopCatalogApplicationService(IWorkshopRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyCollection<Customer>> ListCustomersAsync(CancellationToken cancellationToken) =>
        await _repository.ListCustomersAsync(cancellationToken);

    public async Task<Customer?> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.GetCustomerByIdAsync(id, trackChanges: false, cancellationToken);

    public async Task<Customer> CreateCustomerAsync(Contracts.UpsertCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = new Customer(request.Name, request.Document, request.Phone, request.Email);
        _repository.AddCustomer(customer);
        await _repository.SaveChangesAsync(cancellationToken);
        return customer;
    }

    public async Task<Customer?> UpdateCustomerAsync(Guid id, Contracts.UpsertCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await _repository.GetCustomerByIdAsync(id, trackChanges: true, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        customer.Update(request.Name, request.Document, request.Phone, request.Email);
        await _repository.SaveChangesAsync(cancellationToken);
        return customer;
    }

    public async Task<bool> DeleteCustomerAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _repository.GetCustomerByIdAsync(id, trackChanges: true, cancellationToken);
        if (customer is null)
        {
            return false;
        }

        _repository.RemoveCustomer(customer);
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<Vehicle>> ListVehiclesAsync(CancellationToken cancellationToken) =>
        await _repository.ListVehiclesAsync(cancellationToken);

    public async Task<Vehicle?> GetVehicleByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.GetVehicleByIdAsync(id, trackChanges: false, cancellationToken);

    public async Task<Vehicle> CreateVehicleAsync(Contracts.UpsertVehicleRequest request, CancellationToken cancellationToken)
    {
        var customer = await _repository.GetCustomerByIdAsync(request.CustomerId, trackChanges: false, cancellationToken);
        if (customer is null)
        {
            throw new KeyNotFoundException("Cliente nao encontrado para vincular veiculo.");
        }

        var vehicle = new Vehicle(request.CustomerId, request.LicensePlate, request.Brand, request.Model, request.Year);
        _repository.AddVehicle(vehicle);
        await _repository.SaveChangesAsync(cancellationToken);
        return vehicle;
    }

    public async Task<Vehicle?> UpdateVehicleAsync(Guid id, Contracts.UpsertVehicleRequest request, CancellationToken cancellationToken)
    {
        var vehicle = await _repository.GetVehicleByIdAsync(id, trackChanges: true, cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        if (vehicle.CustomerId != request.CustomerId)
        {
            throw new DomainException("O cliente do veiculo nao pode ser alterado nesse endpoint.");
        }

        vehicle.Update(request.LicensePlate, request.Brand, request.Model, request.Year);
        await _repository.SaveChangesAsync(cancellationToken);
        return vehicle;
    }

    public async Task<bool> DeleteVehicleAsync(Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await _repository.GetVehicleByIdAsync(id, trackChanges: true, cancellationToken);
        if (vehicle is null)
        {
            return false;
        }

        _repository.RemoveVehicle(vehicle);
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<RepairService>> ListRepairServicesAsync(CancellationToken cancellationToken) =>
        await _repository.ListRepairServicesAsync(cancellationToken);

    public async Task<RepairService?> GetRepairServiceByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.GetRepairServiceByIdAsync(id, trackChanges: false, cancellationToken);

    public async Task<RepairService> CreateRepairServiceAsync(Contracts.UpsertRepairServiceRequest request, CancellationToken cancellationToken)
    {
        var service = new RepairService(request.Name, request.Description, request.LaborPrice, request.AverageDurationMinutes);
        _repository.AddRepairService(service);
        await _repository.SaveChangesAsync(cancellationToken);
        return service;
    }

    public async Task<RepairService?> UpdateRepairServiceAsync(Guid id, Contracts.UpsertRepairServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await _repository.GetRepairServiceByIdAsync(id, trackChanges: true, cancellationToken);
        if (service is null)
        {
            return null;
        }

        service.Update(request.Name, request.Description, request.LaborPrice, request.AverageDurationMinutes);
        await _repository.SaveChangesAsync(cancellationToken);
        return service;
    }

    public async Task<bool> DeleteRepairServiceAsync(Guid id, CancellationToken cancellationToken)
    {
        var service = await _repository.GetRepairServiceByIdAsync(id, trackChanges: true, cancellationToken);
        if (service is null)
        {
            return false;
        }

        _repository.RemoveRepairService(service);
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<PartSupply>> ListPartSuppliesAsync(CancellationToken cancellationToken) =>
        await _repository.ListPartSuppliesAsync(cancellationToken);

    public async Task<PartSupply?> GetPartSupplyByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.GetPartSupplyByIdAsync(id, trackChanges: false, cancellationToken);

    public async Task<PartSupply> CreatePartSupplyAsync(Contracts.UpsertPartSupplyRequest request, CancellationToken cancellationToken)
    {
        var partSupply = new PartSupply(request.Name, request.UnitPrice, request.StockQuantity);
        _repository.AddPartSupply(partSupply);
        await _repository.SaveChangesAsync(cancellationToken);
        return partSupply;
    }

    public async Task<PartSupply?> UpdatePartSupplyAsync(Guid id, Contracts.UpsertPartSupplyRequest request, CancellationToken cancellationToken)
    {
        var partSupply = await _repository.GetPartSupplyByIdAsync(id, trackChanges: true, cancellationToken);
        if (partSupply is null)
        {
            return null;
        }

        partSupply.Update(request.Name, request.UnitPrice, request.StockQuantity);
        await _repository.SaveChangesAsync(cancellationToken);
        return partSupply;
    }

    public async Task<bool> DeletePartSupplyAsync(Guid id, CancellationToken cancellationToken)
    {
        var partSupply = await _repository.GetPartSupplyByIdAsync(id, trackChanges: true, cancellationToken);
        if (partSupply is null)
        {
            return false;
        }

        _repository.RemovePartSupply(partSupply);
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }
}
