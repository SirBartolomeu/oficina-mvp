using OficinaMvp.Domain.Exceptions;

namespace OficinaMvp.Domain.Entities;

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
        DateTime createdAtUtc,
        string? branchId = null)
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
        BranchId = string.IsNullOrWhiteSpace(branchId) ? null : branchId.Trim();
        if (BranchId is { Length: > 64 })
        {
            throw new DomainException("O identificador da filial deve ter no máximo 64 caracteres.");
        }
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Status = WorkOrderStatus.Received;
        ProcessState = WorkOrderProcessState.AwaitingBudget;
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
    public WorkOrderProcessState ProcessState { get; private set; }
    public string Notes { get; private set; } = string.Empty;
    public string? BranchId { get; private set; }
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

    public Customer Customer { get; private set; } = null!;
    public Vehicle Vehicle { get; private set; } = null!;
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
        ProcessState = WorkOrderProcessState.AwaitingApproval;
        ChangeStatus(WorkOrderStatus.AwaitingApproval, changedAtUtc, "Orçamento enviado para aprovação do cliente.");
    }

    public void ApproveBudget(DateTime changedAtUtc)
    {
        EnsureCurrentStatus(WorkOrderStatus.AwaitingApproval);
        ApprovedAtUtc = changedAtUtc;
        ExecutionStartedAtUtc = changedAtUtc;
        ProcessState = WorkOrderProcessState.InExecution;
        ChangeStatus(WorkOrderStatus.InExecution, changedAtUtc, "Cliente aprovou o orçamento. Serviço em execução.");
    }

    public void ApproveBudgetForPayment(DateTime changedAtUtc)
    {
        EnsureCurrentStatus(WorkOrderStatus.AwaitingApproval);
        EnsureProcessState(WorkOrderProcessState.AwaitingApproval);
        ApprovedAtUtc = changedAtUtc;
        ProcessState = WorkOrderProcessState.AwaitingStockReservation;
        RecordProcessNote(changedAtUtc, "Orçamento aprovado. Aguardando reserva de peças.");
    }

    public void ConfirmStockReservation(DateTime changedAtUtc)
    {
        EnsureProcessState(WorkOrderProcessState.AwaitingStockReservation);
        ProcessState = WorkOrderProcessState.AwaitingPayment;
        RecordProcessNote(changedAtUtc, "Peças reservadas. Aguardando pagamento.");
    }

    public void ConfirmPayment(DateTime changedAtUtc)
    {
        EnsureProcessState(WorkOrderProcessState.AwaitingPayment);
        ProcessState = WorkOrderProcessState.ReadyForExecution;
        RecordProcessNote(changedAtUtc, "Pagamento confirmado. Aguardando execução.");
    }

    public void StartExecutionAfterPayment(DateTime changedAtUtc)
    {
        EnsureProcessState(WorkOrderProcessState.ReadyForExecution);
        ExecutionStartedAtUtc = changedAtUtc;
        ProcessState = WorkOrderProcessState.InExecution;
        ChangeStatus(WorkOrderStatus.InExecution, changedAtUtc, "Pagamento confirmado. Serviço em execução.");
    }

    public void CancelBeforeExecution(DateTime changedAtUtc, string reason)
    {
        if (ProcessState is not WorkOrderProcessState.AwaitingStockReservation
            and not WorkOrderProcessState.AwaitingPayment
            and not WorkOrderProcessState.ReadyForExecution)
        {
            throw new DomainException("A OS só pode ser cancelada antes do início da execução.");
        }

        ProcessState = WorkOrderProcessState.Canceled;
        RecordProcessNote(changedAtUtc, $"OS cancelada: {NormalizeOptional(reason)}");
    }

    public void RegisterBudgetRefusal(DateTime changedAtUtc, string? reason)
    {
        EnsureCurrentStatus(WorkOrderStatus.AwaitingApproval);
        EnsureProcessState(WorkOrderProcessState.AwaitingApproval);

        var note = string.IsNullOrWhiteSpace(reason)
            ? "Cliente recusou o orcamento. OS permanece aguardando nova aprovacao."
            : $"Cliente recusou o orcamento. Motivo: {reason.Trim()}";

        UpdatedAtUtc = changedAtUtc;
        StatusHistory.Add(new WorkOrderStatusHistory(Status, changedAtUtc, note));
    }

    public void Finalize(DateTime changedAtUtc)
    {
        EnsureCurrentStatus(WorkOrderStatus.InExecution);
        FinalizedAtUtc = changedAtUtc;
        ProcessState = WorkOrderProcessState.Completed;
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

    private void EnsureProcessState(WorkOrderProcessState expectedState)
    {
        if (ProcessState != expectedState)
        {
            throw new DomainException($"Transição de processo inválida. Estado atual: {ProcessState}.");
        }
    }

    private void RecordProcessNote(DateTime changedAtUtc, string note)
    {
        UpdatedAtUtc = changedAtUtc;
        StatusHistory.Add(new WorkOrderStatusHistory(Status, changedAtUtc, note));
    }

    private void ChangeStatus(WorkOrderStatus newStatus, DateTime changedAtUtc, string note)
    {
        Status = newStatus;
        UpdatedAtUtc = changedAtUtc;
        StatusHistory.Add(new WorkOrderStatusHistory(newStatus, changedAtUtc, note));
    }

    private static string NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
