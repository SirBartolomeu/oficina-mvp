using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Oficina.Execution.Persistence;

public sealed class ExecutionDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ExecutionDbContext>
{
    public ExecutionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ExecutionDbContext>()
            .UseNpgsql("Host=localhost;Database=oficina_execution;Username=design;Password=design")
            .Options;
        return new ExecutionDbContext(options);
    }
}
