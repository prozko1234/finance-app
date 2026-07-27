namespace FinanceApp.Domain.Budgeting;

/// The schemes financial advisers actually recommend, as data. Adding one is a row here,
/// never a branch in the calculator — that is the whole point of modelling buckets.
public static class AllocationPresets
{
    /// The default: one Spending bucket at 100%, which is the app's behaviour before
    /// schemes existed. Someone who does not want to divide anything never has to.
    public const string DailyNormOnly = "daily-norm-only";

    public static readonly IReadOnlyList<AllocationPreset> All =
    [
        new(DailyNormOnly, "Тільки денна норма", "Весь бюджет — на витрати, як і було",
        [
            new("На витрати", BucketKind.Spending, 100m),
        ]),
        new("50-30-20", "50/30/20", "Потреби / бажання / заощадження — Warren & Tyagi",
        [
            new("Потреби", BucketKind.Spending, 50m),
            new("Бажання", BucketKind.Spending, 30m),
            new("Заощадження", BucketKind.Savings, 20m),
        ]),
        new("70-20-10", "70/20/10", "Витрати / заощадження / борг або донат",
        [
            new("Витрати", BucketKind.Spending, 70m),
            new("Заощадження", BucketKind.Savings, 20m),
            new("Борг або донат", BucketKind.Debt, 10m),
        ]),
        new("80-20", "80/20 «спершу собі»", "20 відклав, решта — без обліку",
        [
            new("Заощадження", BucketKind.Savings, 20m),
            new("Решта", BucketKind.Spending, 80m),
        ]),
        new("60-solution", "60% Solution", "60 на зобовʼязання + чотири по 10 — Richard Jenkins",
        [
            new("Зобовʼязання", BucketKind.Spending, 60m),
            new("Пенсія", BucketKind.Investing, 10m),
            new("Довгі заощадження", BucketKind.Savings, 10m),
            new("Нерегулярні витрати", BucketKind.Other, 10m),
            new("Розваги", BucketKind.Spending, 10m),
        ]),
    ];

    public static AllocationPreset? Find(string key) =>
        All.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// A ready-to-save scheme built from a preset.
    public static AllocationScheme ToScheme(this AllocationPreset preset, bool isActive) => new()
    {
        Name = preset.Name,
        Preset = preset.Key,
        IsActive = isActive,
        UpdatedAt = DateTimeOffset.UtcNow,
        Buckets = preset.Buckets
            .Select((b, i) => new AllocationBucket
            {
                Name = b.Name, Kind = b.Kind, Percent = b.Percent, SortOrder = i,
            })
            .ToList(),
    };
}

public record AllocationPreset(
    string Key, string Name, string Hint, IReadOnlyList<PresetBucket> Buckets);

public record PresetBucket(string Name, BucketKind Kind, decimal Percent);
