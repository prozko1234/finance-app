using FinanceApp.Domain;

namespace FinanceApp.Application.Contracts;

/// Create/update transaction request. Currency is a 3-letter ISO code.
/// Date is optional (defaults to today). Base amount and rate are computed on the server.
public record SaveTransactionRequest(
    decimal Amount,
    string Currency,
    int CategoryId,
    Frequency Frequency,
    DateOnly? Date,
    string? Merchant,
    string? Note,
    /// Which envelope the money came out of. Null (the default) — from what is free to
    /// spend, which is what almost every expense is.
    int? EnvelopeId = null);

public record SaveIncomeRequest(
    decimal Amount,
    bool AmountIncludesVat,
    string Currency,
    DateOnly? Date,
    string? Note);

public record TransactionResponse(
    int Id,
    string Kind,
    decimal? GrossWithVat,
    decimal? VatAmount,
    decimal AmountOriginal,
    string CurrencyOriginal,
    decimal AmountBase,
    decimal FxRate,
    DateOnly FxDate,
    int CategoryId,
    string CategoryName,
    int? EnvelopeId,
    string? EnvelopeName,
    Frequency Frequency,
    string Source,
    DateOnly Date,
    string? Merchant,
    string? Note,
    DateTimeOffset CreatedAt,
    /// The same amount as the user reads it, converted at THIS transaction's date — so a
    /// July expense keeps its July size. Equals AmountBase while reading in PLN.
    decimal AmountDisplay,
    string DisplayCurrency,
    /// Income only: whether the figure the user typed was the gross one. Derived rather than
    /// stored — the two candidates differ by the whole VAT, so there is nothing to guess — and
    /// sent so the edit form opens on the same toggle the invoice was written with.
    bool AmountIncludesVat = false,
    /// The emoji the category actually carries. Sent with the row because the list used to
    /// guess it from the category NAME against a hard-coded table, so every category the user
    /// made themselves — and every renamed one — showed the same 📦.
    string? CategoryIcon = null);

public record CategoryResponse(int Id, string Name, string? Icon, string? Color, int SortOrder, bool IsSystem);

public record SaveCategoryRequest(string Name, string? Icon, string? Color);



/// Where this month's income went before it became a budget. Explains the gap between
/// "money on the account" and "money you may actually spend". Null when there is no
/// income this month (or no usable tax profile) — then the budget is just the manual one.
public record MonthTaxBreakdown(
    decimal Gross,        // скільки реально прийшло на рахунок (з VAT)
    decimal Revenue,      // przychód — без VAT, база для податків
    decimal Vat,
    decimal ZusSocial,
    decimal Health,
    decimal Tax,
    decimal SetAside,     // VAT + ZUS + здоровотна + податок
    decimal TakeHome,     // = MonthlyBudget
    /// This split's currency is always the base one. The Polish engine works in złoty, and
    /// these are the figures the bookkeeper will see, so they are not converted with the rest
    /// of the screen.
    string Currency,
    /// The year the built-in ZUS, health-contribution and PIT-threshold figures were checked
    /// against. There is no machine-readable API for them (verified), so the only guard against
    /// quietly stale numbers is to say which year they are for and let the app notice when the
    /// year has moved on.
    int RatesYear = 0);

public record SafeToSpendResponse(
    DateOnly Date,
    string Currency,
    bool BudgetSet,
    decimal? PeriodBudget,
    decimal SpentThisPeriod,
    decimal ReservedRecurring,
    decimal? RemainingThisPeriod,
    int DaysLeftInPeriod,
    decimal? DailyNorm,
    decimal SpentToday,
    decimal? LeftToday,
    decimal? TomorrowIfStop,
    decimal? TomorrowIfOnPlan,
    MonthTaxBreakdown? MonthTaxes,
    IReadOnlyList<EnvelopeSummary> Envelopes,
    AllocationSummary? Allocation = null,
    /// The day the count starts from — the period's first day, unless the user began
    /// mid-period by counting what they had.
    DateOnly? WindowStart = null,
    /// True when the budget is "what I have right now" rather than income or a set budget.
    bool FromOpeningBalance = false,
    /// The period these figures cover. Sent so the screen can name it («10.07 – 09.08»)
    /// instead of saying "місяць" to someone whose money arrives on the 10th.
    DateOnly PeriodStart = default,
    DateOnly PeriodEnd = default,
    /// Last period's leftover, when it is still waiting to be told where to go. Null once the
    /// question has been answered — including when the answer was "не рахувати".
    CarryoverResponse? Carryover = null);

/// Where the month's budget went before the daily norm was computed — the "куди пішов
/// бюджет" row. Null-ish case (the default one-bucket scheme) still comes through, so the
/// UI can decide on its own whether a single 100% bucket is worth showing.
public record AllocationSummary(
    string SchemeName,
    string? Preset,
    decimal Spendable,
    decimal Reserved,
    IReadOnlyList<BucketShareResponse> Buckets);

public record BucketShareResponse(
    int Id, string Name, string Kind, decimal Percent, decimal Amount);

/// The scheme screen: what is active now, and the ready-made schemes to switch to.
public record AllocationResponse(
    AllocationSchemeResponse Active,
    IReadOnlyList<AllocationPresetResponse> Presets);

public record AllocationSchemeResponse(
    string Name, string? Preset, IReadOnlyList<AllocationBucketResponse> Buckets);

public record AllocationBucketResponse(string Name, string Kind, decimal Percent);

public record AllocationPresetResponse(
    string Key, string Name, string Hint, IReadOnlyList<AllocationBucketResponse> Buckets);

/// Either a preset key, or a name plus the user's own buckets.
public record SaveAllocationRequest(
    string? Preset = null, string? Name = null, IReadOnlyList<AllocationBucketResponse>? Buckets = null);

/// The savings envelope, shown on its own: a balance that survives across months,
/// plus how much of this month's goal is still being held back from safe-to-spend.
public record SavingsSummary(
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve);

/// One pot on the home screen: what has piled up in it, and how this month is going.
/// <param name="IsFromScheme">The active scheme has a bucket by this name, so it owns both the
/// goal and the name — the screen offers neither renaming nor putting the pot away.</param>
public record EnvelopeSummary(
    int Id,
    string Name,
    string Kind,
    bool IsDefault,
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve,
    bool IsFromScheme = false,
    EnvelopeTargetResponse? Target = null);

/// What the jar is being filled up to, and what that asks of this period.
/// <param name="PerPeriod">What has to go in each remaining period to arrive on time. 0 when
/// the target has no date, or is already met.</param>
/// <param name="Overdue">The date has gone by with money still missing — said out loud rather
/// than quietly turned into a bigger monthly figure.</param>
public record EnvelopeTargetResponse(
    decimal Amount,
    DateOnly? Date,
    decimal Remaining,
    int PeriodsLeft,
    decimal PerPeriod,
    bool Reached,
    bool Overdue);

/// Moving money between jars: one act, written as two movements that carry the same key.
public record TransferRequest(
    int FromEnvelopeId,
    int ToEnvelopeId,
    decimal Amount,
    string? Currency = null,
    DateOnly? Date = null,
    string? Note = null);

/// A target, or the end of one: a null amount takes it off. Currency is optional and means the
/// one the user is reading in.
public record SetEnvelopeTargetRequest(decimal? Amount, string? Currency = null, DateOnly? Date = null);

/// A pot made by hand: a name and what sort of pot it is. Kind is a <c>BucketKind</c> name and
/// may be anything except Spending — a pot for money being spent is just the daily norm.
public record SaveEnvelopeRequest(string Name, string Kind);

public record EnvelopeResponse(int Id, string Name, string Kind, bool IsDefault);

/// "How much I have right now, until the end of the month" — the mid-month start.
/// Currency is optional and defaults to the one the user is reading in.
public record SetOpeningBalanceRequest(decimal Amount, string? Currency = null, DateOnly? Date = null);

public record OpeningBalanceResponse(
    bool IsSet, decimal? Amount, string Currency, DateOnly? Date, bool AppliesNow);

public record SaveSavingsPlanRequest(string Mode, decimal Value, bool Active);

/// Currency is optional: most movements are in base currency, and an omitted field
/// must not turn into a validation error on the common path.
/// <param name="AlreadySetAside">True for money that was put away BEFORE it was written down
/// — an old pot being recorded so the balance is right. It joins the jar without being taken
/// out of this period's budget a second time. False (the default) means the money is leaving
/// spendable money now, which is what an ordinary deposit is.</param>
public record SaveSavingsEntryRequest(
    string Kind, decimal Amount, DateOnly? Date, string? Note, string? Currency = null,
    /// Which pot the money goes into. Omitted = the default envelope.
    int? EnvelopeId = null,
    bool AlreadySetAside = false);

public record SavingsEntryResponse(
    int Id,
    DateOnly Date,
    string Kind,
    /// In base currency — what this movement did to the balance.
    decimal Amount,
    /// What the user actually typed, and in which currency.
    decimal AmountOriginal,
    string CurrencyOriginal,
    string? Note,
    int EnvelopeId = 0,
    string EnvelopeName = "",
    /// Written by the app carrying out the scheme, not by hand. Editing or deleting one is
    /// undone by the next page load, so the UI must not offer either.
    bool IsAuto = false,
    /// One half of a move between jars. Editable only as a whole — deleting it takes the other
    /// half with it, because money that left one jar and arrived in no other is not a fact.
    bool IsTransfer = false,
    /// Money that was already put away before it was written down, so it joined the balance
    /// without being taken out of any period's budget.
    bool AlreadySetAside = false);

public record SavingsResponse(
    string Mode,
    decimal Value,
    bool Active,
    decimal Balance,
    decimal MonthGoal,
    decimal DepositedThisMonth,
    decimal StillToReserve,
    string Currency,
    IReadOnlyList<SavingsEntryResponse> Recent,
    /// Every pot, not only the default one — the screen has to let money into the pension
    /// bucket too, or the scheme reserves for something that can never be filled.
    IReadOnlyList<EnvelopeSummary> Envelopes,
    /// Name of the allocation scheme that dictates the goal, or null when the plan below
    /// still decides it. Set = the plan's own value is ignored, and the UI must say so.
    string? GoalFromScheme = null,
    /// The day a balance was counted, when that is what stood the plan down until the next
    /// payday. Otherwise null. Without it the screen shows a live plan next to a goal of 0
    /// and looks broken — the reason has to be on screen, not only in the code.
    DateOnly? PlanPausedFrom = null);

public record SaveRecurringRequest(
    decimal Amount,
    string Currency,
    int CategoryId,
    /// The first charge. For a monthly or yearly rule its day is the day it lands on;
    /// for a weekly one its weekday is the weekday.
    DateOnly StartsOn,
    string? Note,
    bool Active,
    // "Expense" (default) or "Income" — a stable monthly salary is recurring too.
    string? Kind = null,
    bool AmountIncludesVat = true,
    /// "Week", "Month" (default) or "Year".
    string? Unit = null,
    /// Every N units: 2 + Week is a fortnight, 3 + Month is a quarter.
    int Interval = 1);

public record TaxProfileResponse(
    string Regime,
    decimal RyczaltRate,
    bool VatPayer,
    decimal VatRate,
    string ZusType,
    decimal ZusSocial,
    decimal HealthContribution,
    bool Chorobowe,
    bool StudentUnder26,
    DateOnly ValidFrom,
    decimal MonthlyContributionsTotal);

public record SaveTaxProfileRequest(
    string Regime,
    decimal RyczaltRate,
    bool VatPayer,
    decimal VatRate,
    string ZusType,
    decimal ZusSocial,
    decimal HealthContribution,
    bool Chorobowe,
    bool StudentUnder26 = false);

public record TaxDefaultsResponse(
    int Year,
    decimal DuzyWithChorobowe,
    decimal DuzyWithoutChorobowe,
    decimal PreferencyjnyWithChorobowe,
    decimal PreferencyjnyWithoutChorobowe,
    decimal HealthUnder60k,
    decimal Health60kTo300k,
    decimal HealthOver300k);

public record CalculateTakeHomeRequest(decimal Amount, bool AmountIncludesVat);

/// Answers "what does this invoice actually add to my budget?" while the user is still typing.
/// Deliberately expressed as a DELTA over the month, not as a standalone invoice calculation:
/// ZUS and health are monthly, so a second invoice adds more take-home than the first one did.
/// Showing a per-invoice figure here would contradict the home screen.
public record IncomePreviewResponse(
    decimal InvoiceGross,     // з VAT — скільки прийде на рахунок
    decimal InvoiceVat,
    decimal InvoiceRevenue,   // przychód цієї фактури
    decimal BudgetBefore,     // бюджет місяця зараз
    decimal BudgetAfter,
    decimal BudgetDelta,      // += до бюджету за цю фактуру
    bool IsFirstIncomeThisMonth,
    MonthTaxBreakdown MonthAfter,
    // The savings plan as it would apply to the month's budget after this invoice — shown
    // (and editable) right in the income form, so putting money aside is not a second trip.
    string SavingsMode,
    decimal SavingsValue,
    bool SavingsActive,
    decimal SavingsGoalAfter,
    string Currency,
    /// Name of the scheme that dictates the goal, or null when the plan still decides it.
    /// Set = the plan editor in the form would change nothing, and the form has to say so.
    string? SavingsFromScheme = null);

public record RecurringResponse(
    int Id,
    decimal AmountOriginal,
    string CurrencyOriginal,
    int CategoryId,
    string CategoryName,
    DateOnly StartsOn,
    string Unit,
    int Interval,
    bool Active,
    string? Note,
    string Kind,
    bool AmountIncludesVat,
    /// The day this will next be charged on, or null while it is paused — a row on hold has
    /// nothing coming. Computed server-side because the day is clamped to the month's length
    /// (the 31st in February) and the screen must not guess that on its own.
    DateOnly? NextChargeOn = null,
    /// Already taken out of this period's budget. The screen says so, because the money is
    /// gone but the row looks exactly the same as one that is still to come.
    bool ChargedThisPeriod = false);

/// App-wide settings. <paramref name="BaseCurrency"/> is what the app stores in; the user
/// only chooses what to read. <paramref name="TaxesInBaseCurrency"/> tells the UI it must
/// say the tax split is still computed in PLN.
/// The statistics screen in one response: the bars, and the breakdown of one month.
/// <paramref name="SelectedMonth"/> is echoed back ("yyyy-MM") because an out-of-range or
/// unparsable request falls back to the current month, and the UI must label what it shows.
/// <param name="SavedBalance">What is actually IN the jars right now, all of it — including
/// money that was put away before this app knew about it. The per-month figures beside it are
/// flow (what moved), and flow deliberately leaves that money out; without a stock figure the
/// screen would total up to less than the jars hold and read as though something was lost.
/// </param>
/// <param name="SavedByCurrency">What went in, kept in the currency it was put in and NOT
/// converted. Someone saving złoty and dollars is told "12 500 zł" by a converted total, and
/// that number hides both what they have and the fact that half of it moves with the rate.
/// Empty when everything was in one currency — then the total already says it.</param>
public record StatsResponse(
    string Currency,
    IReadOnlyList<MonthStatsResponse> Months,
    string SelectedMonth,
    decimal SelectedExpense,
    IReadOnlyList<CategoryStatsResponse> Categories,
    decimal SavedBalance = 0m,
    IReadOnlyList<CurrencyAmountResponse>? SavedByCurrency = null);

/// An amount left in the currency it was entered in.
public record CurrencyAmountResponse(string Currency, decimal Amount);

/// Income is revenue (przychód, VAT excluded) — the same number the budget is built from,
/// so the bars cannot claim a month earned more than the home screen let the user spend.
/// <param name="SavedByPlan">What the allocation scheme moved into jars by itself this month.
/// This is the scheme's promise actually kept, so it is reported apart from the rest.</param>
/// <param name="SavedByHand">Movements into and out of jars the user made themselves, net of
/// withdrawals and of anything paid straight out of a jar. Can be negative: a month where a
/// jar was raided saved less than nothing, and hiding that would flatter the figure.</param>
public record MonthStatsResponse(
    string Month,
    decimal Income,
    decimal Expense,
    decimal Net,
    decimal SavedByPlan = 0m,
    decimal SavedByHand = 0m);

/// <param name="Decision">"ToEnvelope", "ToBudget" or "Ignore".</param>
public record DecideCarryoverRequest(string Decision, int? EnvelopeId = null);

/// Last period's leftover, waiting to be told where to go. Rides along with the summary so the
/// home screen needs no second request to know whether to ask.
/// <param name="EnvelopeName">The jar the default answer would put it in, named so the button
/// can say where the money is going rather than just "відкласти".</param>
public record CarryoverResponse(
    decimal Amount, DateOnly FromStart, DateOnly FromEnd, string EnvelopeName);

/// <param name="Count">How many purchases made up <paramref name="Amount"/>. Together the two
/// separate "ходжу часто" from "ходжу дорого", which are optimised in opposite ways.</param>
/// <param name="Typical">What this category usually costs in a month — the median of the three
/// calendar months before the selected one, at the same rate as <paramref name="Amount"/> so the
/// two can be subtracted. Null when there is no history to call anything typical yet (see
/// <see cref="Stats.StatsService"/>), and the UI must then show no comparison at all rather
/// than compare against a zero.</param>
public record CategoryStatsResponse(
    int CategoryId, string Name, string? Icon, decimal Amount, decimal Percent, int Count,
    decimal? Typical = null);

/// A one-tap shortcut on the home screen: a category the user has actually been using lately.
/// <param name="Days">The window it was counted over, so the screen can say so out loud —
/// "часті" with no period behind it is a claim the user cannot check.</param>
public record FrequentCategoryResponse(
    int CategoryId, string Name, string? Icon, int Uses, int Days);

public record AppSettingsResponse(
    string DisplayCurrency,
    string BaseCurrency,
    bool TaxesInBaseCurrency,
    /// <summary>Day of the month the money arrives — when the budget period starts.</summary>
    int PeriodStartDay,
    /// <summary>The period that day produces right now, so the UI can say it out loud
    /// instead of making the user work it out from a number.</summary>
    DateOnly PeriodStart,
    DateOnly PeriodEnd);

public record SetDisplayCurrencyRequest(string Currency);

public record SetPeriodStartDayRequest(int Day);

/// One budget period in an envelope's life: what moved in or out, and what was in it when
/// the period ended.
public record EnvelopePeriodResponse(
    DateOnly Start, DateOnly End, decimal Moved, decimal BalanceAfter);

/// One line of a statement as the preview screen shows it. The amount keeps the sign the
/// bank wrote: it is what says whether the row is money in or money out, and hiding it would
/// make the screen argue with the file the user is looking at.
/// <param name="DuplicateOfId">An existing transaction that looks like this one — entered by
/// hand or imported before. Non-null means the row is preselected as "skip".</param>
public record ImportRowPreview(
    int Line,
    DateOnly Date,
    decimal Amount,
    string Currency,
    /// Exactly what the bank wrote, kept so a row can always be checked against the file.
    string Description,
    /// The shop, tidied for reading — no branch number, no bank boilerplate.
    string Merchant,
    /// What rows of the same shop are grouped by, and what a learned rule is keyed on.
    string MerchantKey,
    string Kind,
    int? DuplicateOfId,
    /// Where this shop was filed last time, or where the built-in list expects it. Null means
    /// nothing knows — and then the screen asks instead of guessing.
    int? SuggestedCategoryId);

public record ImportProblemResponse(int Line, string Reason, string Raw);

/// What the reader made of the file. The shape it detected is reported on purpose: when the
/// import looks wrong, "я прочитав це як ; у windows-1250" is the sentence that explains why.
public record ImportPreviewResponse(
    IReadOnlyList<ImportRowPreview> Rows,
    IReadOnlyList<ImportProblemResponse> Problems,
    string Delimiter,
    bool HeaderFound,
    string Encoding,
    IReadOnlyList<string> Columns);

/// <param name="Amount">Signed, as in the preview: negative is an expense.</param>
public record ImportRowRequest(
    int Line,
    DateOnly Date,
    decimal Amount,
    string Currency,
    int CategoryId,
    string? Note,
    bool AmountIncludesVat = true);

public record CommitImportRequest(IReadOnlyList<ImportRowRequest> Rows);

public record ImportResultResponse(
    int Created,
    int Failed,
    IReadOnlyList<ImportProblemResponse> Problems);
