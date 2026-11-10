namespace Oficina.Billing;

public enum BudgetStatus { AwaitingApproval = 1, Approved = 2, Refused = 3 }

public sealed record BudgetLine(Guid ItemId, string Name, int Quantity, long UnitPriceCents);

public sealed record Budget(
    Guid WorkOrderId,
    int Version,
    string? BranchId,
    long TotalCents,
    BudgetStatus Status,
    IReadOnlyCollection<BudgetLine> Lines,
    DateTime CreatedAtUtc)
{
    public static Budget Create(Guid workOrderId, int version, string? branchId, IReadOnlyCollection<BudgetLine> lines)
    {
        if (workOrderId == Guid.Empty || version <= 0 || lines.Count == 0)
            throw new ArgumentException("Orçamento requer OS, versão e itens.");
        if (lines.Any(line => line.ItemId == Guid.Empty || string.IsNullOrWhiteSpace(line.Name) ||
                              line.Quantity <= 0 || line.UnitPriceCents < 0))
            throw new ArgumentException("Item do orçamento inválido.");
        var total = lines.Sum(line => checked(line.UnitPriceCents * line.Quantity));
        return new Budget(workOrderId, version, branchId, total, BudgetStatus.AwaitingApproval,
            lines.ToArray(), DateTime.UtcNow);
    }

    public Budget Decide(bool approved)
    {
        if (Status != BudgetStatus.AwaitingApproval)
            throw new InvalidOperationException("Orçamento já decidido.");
        return this with { Status = approved ? BudgetStatus.Approved : BudgetStatus.Refused };
    }
}
