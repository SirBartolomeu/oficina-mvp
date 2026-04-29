namespace OficinaMvp.Api.Application.Contracts;

public sealed record UpsertRepairServiceRequest(string Name, string Description, decimal LaborPrice, int AverageDurationMinutes);
public sealed record RepairServiceResponse(Guid Id, string Name, string Description, decimal LaborPrice, int AverageDurationMinutes);
