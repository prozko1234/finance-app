using FinanceApp.Api.Contracts;
using FinanceApp.Domain;
using FinanceApp.Domain.Fx;
using FinanceApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Endpoints;

public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/transactions").WithTags("Transactions");

        g.MapGet("/", async (AppDbContext db, int take = 50) =>
        {
            var items = await db.Transactions
                .Include(t => t.Category)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                .Take(Math.Clamp(take, 1, 200))
                .ToListAsync();
            return Results.Ok(items.Select(ToResponse));
        });

        g.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var t = await db.Transactions.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id);
            return t is null ? Results.NotFound() : Results.Ok(ToResponse(t));
        });

        g.MapPost("/", async (SaveTransactionRequest req, AppDbContext db, IFxConverter fx) =>
        {
            var (ok, error, date, conv) = await PrepareAsync(req, db, fx, fallbackDate: null);
            if (!ok) return Results.BadRequest(new { error });

            var tx = new Transaction
            {
                AmountOriginal = req.Amount,
                CurrencyOriginal = req.Currency.ToUpperInvariant(),
                AmountBase = conv!.AmountBase,
                FxRate = conv.Rate,
                FxDate = conv.RateDate,
                CategoryId = req.CategoryId,
                Priority = req.Priority,
                Frequency = req.Frequency,
                Source = TxSource.Manual,
                Date = date,
                MerchantRaw = req.Merchant,
                Note = req.Note,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Transactions.Add(tx);
            await db.SaveChangesAsync();
            await db.Entry(tx).Reference(t => t.Category).LoadAsync();
            return Results.Created($"/api/transactions/{tx.Id}", ToResponse(tx));
        });

        g.MapPut("/{id:int}", async (int id, SaveTransactionRequest req, AppDbContext db, IFxConverter fx) =>
        {
            var tx = await db.Transactions.FindAsync(id);
            if (tx is null) return Results.NotFound();

            var (ok, error, date, conv) = await PrepareAsync(req, db, fx, fallbackDate: tx.Date);
            if (!ok) return Results.BadRequest(new { error });

            tx.AmountOriginal = req.Amount;
            tx.CurrencyOriginal = req.Currency.ToUpperInvariant();
            tx.AmountBase = conv!.AmountBase;
            tx.FxRate = conv.Rate;
            tx.FxDate = conv.RateDate;
            tx.CategoryId = req.CategoryId;
            tx.Priority = req.Priority;
            tx.Frequency = req.Frequency;
            tx.Date = date;
            tx.MerchantRaw = req.Merchant;
            tx.Note = req.Note;
            await db.SaveChangesAsync();
            await db.Entry(tx).Reference(t => t.Category).LoadAsync();
            return Results.Ok(ToResponse(tx));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var tx = await db.Transactions.FindAsync(id);
            if (tx is null) return Results.NotFound();
            db.Transactions.Remove(tx);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return app;
    }

    /// Спільна валідація + конвертація для POST/PUT.
    private static async Task<(bool ok, string? error, DateOnly date, FxConversion? conv)> PrepareAsync(
        SaveTransactionRequest req, AppDbContext db, IFxConverter fx, DateOnly? fallbackDate)
    {
        if (req.Amount <= 0)
            return (false, "Сума має бути більшою за 0.", default, null);
        if (string.IsNullOrWhiteSpace(req.Currency) || req.Currency.Length != 3)
            return (false, "Валюта має бути 3-літерним ISO-кодом.", default, null);
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            return (false, $"Категорію {req.CategoryId} не знайдено.", default, null);

        var date = req.Date ?? fallbackDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        try
        {
            var conv = await fx.ConvertToBaseAsync(req.Amount, req.Currency, date);
            return (true, null, date, conv);
        }
        catch (NotSupportedException ex)
        {
            return (false, ex.Message, default, null);
        }
    }

    private static TransactionResponse ToResponse(Transaction t) => new(
        t.Id, t.AmountOriginal, t.CurrencyOriginal, t.AmountBase, t.FxRate, t.FxDate,
        t.CategoryId, t.Category?.Name ?? "", t.Priority, t.Frequency, t.Source.ToString(),
        t.Date, t.MerchantRaw, t.Note, t.CreatedAt);
}
