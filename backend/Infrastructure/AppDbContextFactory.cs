using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinanceApp.Infrastructure;

/// Used only by the `dotnet ef` tool (design-time) to create migrations without
/// starting the whole application.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=financeapp.db")
            .Options;
        return new AppDbContext(options);
    }
}
