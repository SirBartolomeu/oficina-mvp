namespace OficinaMvp.Domain.Entities;

public enum WorkOrderProcessState
{
    AwaitingBudget = 1,
    AwaitingApproval = 2,
    AwaitingStockReservation = 3,
    AwaitingPayment = 4,
    ReadyForExecution = 5,
    InExecution = 6,
    Completed = 7,
    Canceled = 8,
    ManualReview = 9
}
