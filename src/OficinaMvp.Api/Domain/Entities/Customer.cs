using OficinaMvp.Api.Domain.Exceptions;
using OficinaMvp.Api.Domain.Validation;

namespace OficinaMvp.Api.Domain.Entities;

public sealed class Customer
{
    private Customer()
    {
    }

    public Customer(string name, string document, string? phone, string? email)
    {
        Id = Guid.NewGuid();
        Update(name, document, phone, email);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Document { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }

    public ICollection<Vehicle> Vehicles { get; private set; } = new List<Vehicle>();

    public void Update(string name, string document, string? phone, string? email)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("O nome do cliente é obrigatório.");
        }

        Name = name.Trim();
        Document = DocumentValidator.Normalize(document);
        Phone = NormalizeOptional(phone);
        Email = NormalizeOptional(email);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
