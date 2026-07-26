using System.Text.Json.Serialization;
using FinanceApp.Api.Common;
using FinanceApp.Api.Endpoints;
using FinanceApp.Application;
using FinanceApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=financeapp.db";

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddOpenApi();

// Unified error model (RFC 7807) + catching of unhandled exceptions.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Enums as strings in JSON (request and response): "Must", "OneOff" instead of numbers.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Allow the frontend (Vite) to call the API during local development.
const string DevCors = "dev";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p => p
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();

// Apply migrations on startup — convenient locally, the DB is created automatically.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // UI for manual testing: /scalar
}

app.UseCors(DevCors);

app.MapGet("/", () => Results.Ok(new { app = "finance-app", status = "ok" }));
app.MapCategoryEndpoints();
app.MapTransactionEndpoints();
app.MapBudgetEndpoints();
app.MapSummaryEndpoints();
app.MapRecurringEndpoints();
app.MapTaxEndpoints();
app.MapSavingsEndpoints();

app.Run();

public partial class Program { } // marker for integration tests (WebApplicationFactory)
