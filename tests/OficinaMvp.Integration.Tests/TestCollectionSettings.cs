using Xunit;

// The process-wide MeterListener in WorkOrderStateDurationTests observes all test fixtures.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
