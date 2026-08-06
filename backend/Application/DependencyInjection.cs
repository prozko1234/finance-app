using System.Reflection;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Auth;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Categories;
using FinanceApp.Application.Import;
using FinanceApp.Application.Common;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Dev;
using FinanceApp.Application.Envelopes;
using FinanceApp.Application.Display;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Settings;
using FinanceApp.Application.Stats;
using FinanceApp.Application.Summaries;
using FinanceApp.Application.Tax;
using FinanceApp.Application.Transactions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IUserProvisioning, UserProvisioningService>();
        services.AddScoped<IInviteService, InviteService>();
        services.AddScoped<IDeviceTokenService, DeviceTokenService>();
        services.AddScoped<IBudgetPeriods, BudgetPeriodResolver>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IOpeningBalanceService, OpeningBalanceService>();
        services.AddScoped<ICarryoverService, CarryoverService>();
        services.AddScoped<ISummaryService, SummaryService>();
        services.AddScoped<IStatsService, StatsService>();
        services.AddScoped<IMonthlyBudget, MonthlyBudget>();
        services.AddScoped<ISavingsService, SavingsService>();
        services.AddScoped<IEnvelopeService, EnvelopeService>();
        services.AddScoped<IAllocationService, AllocationService>();
        services.AddScoped<IDevDataService, DevDataService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IRecurringService, RecurringService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IMoneyViewFactory, MoneyViewFactory>();
        services.AddScoped<IRecurringMaterializer, RecurringMaterializer>();
        services.AddScoped<ITaxService, TaxService>();

        // Register every AbstractValidator<T> found in this assembly.
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}
