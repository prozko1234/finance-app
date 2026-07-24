using System.Text.Json.Serialization;
using FinanceApp.Api.Endpoints;
using FinanceApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=financeapp.db";

builder.Services.AddInfrastructure(connectionString);
builder.Services.AddOpenApi();

// Enum-и як рядки в JSON (запит і відповідь): "Must", "OneOff" замість чисел.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Дозволяємо фронтенду (Vite) звертатись до API під час локальної розробки.
const string DevCors = "dev";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p => p
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Застосовуємо міграції при старті — зручно локально, БД створюється сама.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(); // UI для ручного тесту: /scalar
}

app.UseCors(DevCors);

app.MapGet("/", () => Results.Ok(new { app = "finance-app", status = "ok" }));
app.MapCategoryEndpoints();
app.MapTransactionEndpoints();
app.MapBudgetEndpoints();
app.MapSummaryEndpoints();

app.Run();

public partial class Program { } // маркер для інтеграційних тестів (WebApplicationFactory)
