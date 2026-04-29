using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Api.Domain.Entities;

public sealed class WorkOrder
{
    private WorkOrder()
    {
    }

    public WorkOrder(
        Guid customerId,
        Guid vehicleId,
        IEnumerable<WorkOrderServiceLine> services,
        IEnumerable<WorkOrderPartLine> parts,
        string? notes,
        DateTime createdAtUtc)
    {
        var serviceItems = services.ToList();
        var partItems = parts.ToList();

        if (!serviceItems.Any() && !partItems.Any())
        {
            throw new DomainException("A OS deve conter ao menos um serviço ou uma peça/insumo.");
        }

        Id = Guid.NewGuid();
        CustomerId = customerId;
        VehicleId = vehicleId;
        Notes = NormalizeOptional(notes);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Status = WorkOrderStatus.Received;
        Services = serviceItems;
        Parts = partItems;
        StatusHistory = new List<WorkOrderStatusHistory>
        {
            new(Status, createdAtUtc, "Ordem de serviço recebida.")
        };

        RecalculateTotals();
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid VehicleId { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public decimal ServicesTotal { get; private set; }
    public decimal PartsTotal { get; private set; }
    public decimal BudgetTotal { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? DiagnosisStartedAtUtc { get; private set; }
    public DateTime? BudgetSentAtUtc { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public DateTime? ExecutionStartedAtUtc { get; private set; }
    public DateTime? FinalizedAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }

    public Customer? Customer { get; private set; }
    public Vehicle? Vehicle { get; private set; }
    public ICollection<WorkOrderServiceLine> Services { get; private set; } = new List<WorkOrderServiceLine>();
    public ICollection<WorkOrderPartLine> Parts { get; private set; } = new List<WorkOrderPartLine>();
    public ICollection<WorkOrderStatusHistory> StatusHistory { get; private set; } = new List<WorkOrderStatusHistory>();

    public void StartDiagnosis(DateTime changedAtUtc)
    {
        EnsureCurrentStatus(WorkOrderStatus.Received);
        DiagnosisStartedAtUtc = changedAtUtc;
        ChangeStatus(WorkOrderStatus.InDiagnosis, changedAtUtc, "Diagnóstico iniciado.");
    }

    public void SendBudget(DateTime changedAtUtc)
    {
        if (Status is not WorkOrderStatus.Received and not WorkOrderStatus.InDiagnosis)
        {
            throw new DomainException("O orçamento só pode ser enviado quando a OS estiver recebida ou em diagnóstico.");
        }

        BudgetSentAtUtc = changedAtUtc;
        ChangeStatus(WorkOrderStatus.AwaitingApproval, changedAtUtc, "Orçamento enviado para aprovação do cliente.");
    }

    public void ApproveBudget(DateTime changedAtUtc)
    {
        EnsureCurrentStatus(WorkOrderStatus.AwaitingApproval);
        ApprovedAtUtc = changedAtUtc;
        ExecutionStartedAtUtc ??= changedAtUtc;
        ChangeStatus(WorkOrderStatus.InExecution, changedAtUtc, "Cliente aprovou o orçamento. Serviço em execução.");
    }

    public void Finalize(DateTime changedAtUtc)
    {
        EnsureCurrentStatus(WorkOrderStatus.InExecution);
        FinalizedAtUtc = changedAtUtc;
        ChangeStatus(WorkOrderStatus.Finalized, changedAtUtc, "Serviço finalizado.");
    }

    public void Deliver(DateTime changedAtUtc)
    {
        EnsureCurrentStatus(WorkOrderStatus.Finalized);
        DeliveredAtUtc = changedAtUtc;
        ChangeStatus(WorkOrderStatus.Delivered, changedAtUtc, "Veículo entregue ao cliente.");
    }

    private void RecalculateTotals()
    {
        ServicesTotal = decimal.Round(Services.Sum(item => item.Subtotal), 2, MidpointRounding.AwayFromZero);
        PartsTotal = decimal.Round(Parts.Sum(item => item.Subtotal), 2, MidpointRounding.AwayFromZero);
        BudgetTotal = decimal.Round(ServicesTotal + PartsTotal, 2, MidpointRounding.AwayFromZero);
    }

    private void EnsureCurrentStatus(WorkOrderStatus expectedStatus)
    {
        if (Status != expectedStatus)
        {
            throw new DomainException($"Transição inválida. Status atual: {Status}.");
        }
    }

    private void ChangeStatus(WorkOrderStatus newStatus, DateTime changedAtUtc, string note)
    {
        Status = newStatus;
        UpdatedAtUtc = changedAtUtc;
        StatusHistory.Add(new WorkOrderStatusHistory(newStatus, changedAtUtc, note));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
