using System.Reflection;
using FinanceApp.Application.Allocations;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Categories;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Dev;
using FinanceApp.Application.Savings;
using FinanceApp.Application.Settings;
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
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<ISummaryService, SummaryService>();
        services.AddScoped<IMonthlyBudget, MonthlyBudget>();
        services.AddScoped<ISavingsService, SavingsService>();
        services.AddScoped<IAllocationService, AllocationService>();
        services.AddScoped<IDevDataService, DevDataService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IRecurringService, RecurringService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IRecurringMaterializer, RecurringMaterializer>();
        services.AddScoped<ITaxService, TaxService>();

        // Register every AbstractValidator<T> found in this assembly.
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}
