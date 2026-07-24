using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinanceApp.Infrastructure;

/// Використовується лише інструментом `dotnet ef` (design-time), щоб створювати
/// міграції без запуску всього застосунку.
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
