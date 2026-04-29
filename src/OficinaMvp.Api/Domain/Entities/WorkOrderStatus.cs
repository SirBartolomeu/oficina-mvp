namespace OficinaMvp.Api.Domain.Entities;

public enum WorkOrderStatus
{
    Received = 1,
    InDiagnosis = 2,
    AwaitingApproval = 3,
    InExecution = 4,
    Finalized = 5,
    Delivered = 6
}
