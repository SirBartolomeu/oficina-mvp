namespace OficinaMvp.Api.Application.Contracts;

public sealed record UpsertCustomerRequest(string Name, string Document, string? Phone, string? Email);
public sealed record CustomerResponse(Guid Id, string Name, string Document, string? Phone, string? Email);
