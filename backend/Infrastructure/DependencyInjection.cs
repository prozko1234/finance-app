using FinanceApp.Domain.Fx;
using FinanceApp.Infrastructure.Fx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));

        // Джерела курсів як typed HttpClient-и (короткий таймаут — не вішаємо ввід транзакції).
        services.AddHttpClient<NbpRateProvider>(c =>
        {
            c.BaseAddress = new Uri("https://api.nbp.pl/api/");
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHttpClient<EcbRateProvider>(c =>
        {
            c.BaseAddress = new Uri("https://www.ecb.europa.eu/");
            c.Timeout = TimeSpan.FromSeconds(5);
        });

        // Порядок реєстрації = порядок фолбеку: спершу NBP, потім ECB.
        services.AddScoped<IFxRateProvider>(sp => sp.GetRequiredService<NbpRateProvider>());
        services.AddScoped<IFxRateProvider>(sp => sp.GetRequiredService<EcbRateProvider>());
        services.AddScoped<IFxConverter, CachingFxConverter>();

        return services;
    }
}
