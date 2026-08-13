using FinanceApp.Application.Abstractions;
using FinanceApp.Domain.Auth;
using FinanceApp.Domain.Fx;
using FinanceApp.Infrastructure.Auth;
using FinanceApp.Infrastructure.Fx;
using FinanceApp.Infrastructure.Push;
using Lib.Net.Http.WebPush;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Stateless and thread-safe: one instance is enough.
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        // Rate sources as typed HttpClients (short timeout — never block a transaction entry).
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

        // Registration order = fallback order: NBP first, then ECB.
        services.AddScoped<IFxRateProvider>(sp => sp.GetRequiredService<NbpRateProvider>());
        services.AddScoped<IFxRateProvider>(sp => sp.GetRequiredService<EcbRateProvider>());
        services.AddScoped<IFxConverter, CachingFxConverter>();

        // Charge reminders. The options bind even when the keys are absent — the background
        // service says so once at startup and stops, rather than throwing on every tick.
        services.AddOptions<VapidOptions>().BindConfiguration(VapidOptions.Section);
        // A typed HttpClient rather than the library's own extension: it does not ship one,
        // and this way the push service gets the same short timeout everything else external
        // has — a slow push endpoint must never hold up the reminder round.
        services.AddHttpClient<PushServiceClient>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHostedService<ChargeReminderService>();

        return services;
    }
}
