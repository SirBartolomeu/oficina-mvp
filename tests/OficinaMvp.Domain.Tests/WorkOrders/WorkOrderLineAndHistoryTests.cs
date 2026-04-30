using OficinaMvp.Api.Domain.Entities;
using OficinaMvp.Api.Domain.Exceptions;

namespace OficinaMvp.Domain.Tests.WorkOrders;

public sealed class WorkOrderLineAndHistoryTests
{
    [Fact]
    public void WorkOrderPartLine_ShouldCalculateSubtotal()
    {
        var line = new WorkOrderPartLine(Guid.NewGuid(), "Filtro", 2, 40m);

        Assert.Equal(80m, line.Subtotal);
    }

    [Fact]
    public void WorkOrderPartLine_ShouldThrow_WhenNameIsInvalid()
    {
        Assert.Throws<DomainException>(() => new WorkOrderPartLine(Guid.NewGuid(), " ", 1, 10m));
    }

    [Fact]
    public void WorkOrderPartLine_ShouldThrow_WhenQuantityIsInvalid()
    {
        Assert.Throws<DomainException>(() => new WorkOrderPartLine(Guid.NewGuid(), "Filtro", 0, 10m));
    }

    [Fact]
    public void WorkOrderPartLine_ShouldThrow_WhenPriceIsInvalid()
    {
        Assert.Throws<DomainException>(() => new WorkOrderPartLine(Guid.NewGuid(), "Filtro", 1, -1m));
    }

    [Fact]
    public void WorkOrderServiceLine_ShouldCalculateSubtotal()
    {
        var line = new WorkOrderServiceLine(Guid.NewGuid(), "Troca de oleo", 2, 120m);

        Assert.Equal(240m, line.Subtotal);
    }

    [Fact]
    public void WorkOrderServiceLine_ShouldThrow_WhenNameIsInvalid()
    {
        Assert.Throws<DomainException>(() => new WorkOrderServiceLine(Guid.NewGuid(), " ", 1, 10m));
    }

    [Fact]
    public void WorkOrderServiceLine_ShouldThrow_WhenQuantityIsInvalid()
    {
        Assert.Throws<DomainException>(() => new WorkOrderServiceLine(Guid.NewGuid(), "Troca", 0, 10m));
    }

    [Fact]
    public void WorkOrderServiceLine_ShouldThrow_WhenPriceIsInvalid()
    {
        Assert.Throws<DomainException>(() => new WorkOrderServiceLine(Guid.NewGuid(), "Troca", 1, -1m));
    }

    [Fact]
    public void WorkOrderStatusHistory_ShouldStoreNote_WhenValid()
    {
        var history = new WorkOrderStatusHistory(WorkOrderStatus.Received, DateTime.UtcNow, "Ordem recebida.");

        Assert.Equal(WorkOrderStatus.Received, history.Status);
        Assert.Equal("Ordem recebida.", history.Note);
    }

    [Fact]
    public void WorkOrderStatusHistory_ShouldThrow_WhenNoteIsInvalid()
    {
        Assert.Throws<DomainException>(() => new WorkOrderStatusHistory(WorkOrderStatus.Received, DateTime.UtcNow, " "));
    }

    [Fact]
    public void DomainException_ShouldSetMessage()
    {
        var exception = new DomainException("erro de dominio");

        Assert.Equal("erro de dominio", exception.Message);
    }
}
