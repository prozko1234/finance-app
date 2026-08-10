using FinanceApp.Application.Auth;
using FinanceApp.Application.Common;
using FinanceApp.Api.Tests.Integration;
using FinanceApp.Application.Contracts;
using FinanceApp.Application.Display;
using FinanceApp.Application.Recurring;
using FinanceApp.Application.Transactions;
using FinanceApp.Domain;
using FinanceApp.Domain.Common;
using FinanceApp.Domain.Tax;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Api.Tests;

/// Correcting an invoice used to mean deleting it and typing it again — and typing it again
/// is where a digit gets lost. The reason it was not simply allowed through the ordinary
/// update is the whole point of these tests: an income row keeps przychód (VAT excluded) in
/// AmountBase, and the expense path would write the gross figure there. Nothing would look
/// broken; the month's budget would just be wrong by the VAT.
public class IncomeEditTests
{
    private static TransactionService Sut(SqliteInMemory mem)
    {
        var fx = new FakeFxConverter();
        return new TransactionService(
            mem.Db, fx, new RecurringMaterializer(mem.Db, fx, new BudgetPeriodResolver(mem.Db)),
            new MoneyViewFactory(mem.Db, fx), new UserProvisioningService(mem.Db));
    }

    /// A VAT payer at 23%, which is what makes the gross and the revenue two different numbers.
    private static async Task VatPayerAsync(SqliteInMemory mem)
    {
        mem.Db.TaxProfiles.Add(new TaxProfile { VatPayer = true, VatRate = 0.23m });
        await mem.Db.SaveChangesAsync();
    }

    private static SaveIncomeRequest Invoice(decimal amount, bool withVat = true, string? note = null) =>
        new(amount, withVat, "PLN", null, note);

    [Fact]
    public async Task Correcting_an_invoice_moves_the_vat_split_with_it()
    {
        using var mem = new SqliteInMemory();
        await VatPayerAsync(mem);
        var created = (await Sut(mem).CreateIncomeAsync(Invoice(12_300m))).Value!;

        var fixedUp = await Sut(mem).UpdateIncomeAsync(created.Id, Invoice(24_600m, note: "два рахунки"));

        Assert.True(fixedUp.IsSuccess);
        var row = await mem.Db.Transactions.FindAsync(created.Id);
        Assert.Equal(24_600m, row!.GrossWithVat);
        Assert.Equal(20_000m, row.AmountBase);    // przychód, а не те, що прийшло на рахунок
        Assert.Equal(4_600m, row.VatAmount);
        Assert.Equal("два рахунки", row.Note);
    }

    /// The expense path knows nothing about VAT, so it must not be the one to touch an invoice.
    [Fact]
    public async Task An_invoice_is_not_edited_as_if_it_were_an_expense()
    {
        using var mem = new SqliteInMemory();
        await VatPayerAsync(mem);
        var created = (await Sut(mem).CreateIncomeAsync(Invoice(12_300m))).Value!;

        var wrongDoor = await Sut(mem).UpdateAsync(created.Id, new SaveTransactionRequest(
            24_600m, "PLN", created.CategoryId, Frequency.OneOff, null, null, null));

        Assert.Equal(ErrorType.Validation, wrongDoor.Error.Type);
        var row = await mem.Db.Transactions.FindAsync(created.Id);
        Assert.Equal(10_000m, row!.AmountBase);   // недоторкане: бюджет не поїхав
        Assert.Equal(2_300m, row.VatAmount);
    }

    [Fact]
    public async Task An_expense_is_not_edited_as_if_it_were_an_invoice()
    {
        using var mem = new SqliteInMemory();
        await VatPayerAsync(mem);
        var category = await mem.Db.Categories.FirstAsync();
        var expense = (await Sut(mem).CreateAsync(new SaveTransactionRequest(
            100m, "PLN", category.Id, Frequency.OneOff, null, null, null))).Value!;

        var wrongDoor = await Sut(mem).UpdateIncomeAsync(expense.Id, Invoice(200m));

        Assert.Equal(ErrorType.Validation, wrongDoor.Error.Type);
    }

    /// The fx rate is fixed at creation and never recomputed; the VAT treatment is the same
    /// kind of fact. Someone who registers for VAT in September must not find that August's
    /// invoice quietly grew a VAT line the moment they corrected a typo in its note.
    [Fact]
    public async Task An_invoice_keeps_the_vat_treatment_it_was_written_under()
    {
        using var mem = new SqliteInMemory();
        mem.Db.TaxProfiles.Add(new TaxProfile { VatPayer = false, VatRate = 0.23m });
        await mem.Db.SaveChangesAsync();
        var created = (await Sut(mem).CreateIncomeAsync(Invoice(10_000m))).Value!;
        Assert.Equal(0m, created.VatAmount);

        // The user registers for VAT afterwards, and only then fixes the amount.
        var profile = await mem.Db.TaxProfiles.FirstAsync();
        profile.VatPayer = true;
        await mem.Db.SaveChangesAsync();

        await Sut(mem).UpdateIncomeAsync(created.Id, Invoice(12_000m));

        var row = await mem.Db.Transactions.FindAsync(created.Id);
        Assert.Equal(0m, row!.VatAmount);          // рахунок лишається без VAT
        Assert.Equal(12_000m, row.AmountBase);
    }

    /// The edit form has to open on the toggle the invoice was written with, and the answer is
    /// recoverable from the row itself: the two candidates differ by the whole VAT.
    [Fact]
    public async Task The_row_remembers_whether_the_typed_figure_was_the_gross_one()
    {
        using var mem = new SqliteInMemory();
        await VatPayerAsync(mem);

        var gross = (await Sut(mem).CreateIncomeAsync(Invoice(12_300m))).Value!;
        var net = (await Sut(mem).CreateIncomeAsync(Invoice(10_000m, withVat: false))).Value!;

        Assert.True(gross.AmountIncludesVat);
        Assert.False(net.AmountIncludesVat);
    }

    [Fact]
    public async Task A_correction_to_zero_is_refused_rather_than_stored()
    {
        using var mem = new SqliteInMemory();
        await VatPayerAsync(mem);
        var created = (await Sut(mem).CreateIncomeAsync(Invoice(12_300m))).Value!;

        var refused = await Sut(mem).UpdateIncomeAsync(created.Id, Invoice(0m));

        Assert.Equal(ErrorType.Validation, refused.Error.Type);
        Assert.Equal(10_000m, (await mem.Db.Transactions.FindAsync(created.Id))!.AmountBase);
    }
}
