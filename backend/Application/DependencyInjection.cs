using System.Reflection;
using FinanceApp.Application.Budgets;
using FinanceApp.Application.Categories;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Summaries;
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
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IRecurringService, RecurringService>();
        services.AddScoped<IRecurringMaterializer, RecurringMaterializer>();

        // Register every AbstractValidator<T> found in this assembly.
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}
