namespace OficinaMvp.Api.Application.Contracts;

public sealed record UpsertVehicleRequest(Guid CustomerId, string LicensePlate, string Brand, string Model, int Year);
public sealed record VehicleResponse(Guid Id, Guid CustomerId, string LicensePlate, string Brand, string Model, int Year);
