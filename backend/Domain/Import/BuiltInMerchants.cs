namespace FinanceApp.Domain.Import;

/// A first guess for shops anyone living in Poland meets in their first week.
///
/// It exists so the very first import is not three hundred rows of "Інше". It is deliberately
/// a fallback, not a rule: anything the user has categorised themselves wins, and every
/// correction becomes a learned rule that outranks this list for good.
///
/// Mapped to category NAMES rather than ids: the seeded categories can be renamed, deleted or
/// replaced, and a list of hard ids would then quietly file the shopping under whatever
/// happens to be id 3 today.
public static class BuiltInMerchants
{
    /// <summary>Merchant key (as <see cref="MerchantKey.From"/> produces) → category name.</summary>
    public static IReadOnlyDictionary<string, string> ByKey { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Продукти
            ["BIEDRONKA"] = Food, ["LIDL"] = Food, ["ZABKA"] = Food, ["ŻABKA"] = Food,
            ["AUCHAN"] = Food, ["CARREFOUR"] = Food, ["KAUFLAND"] = Food, ["NETTO"] = Food,
            ["ALDI"] = Food, ["DINO"] = Food, ["STOKROTKA"] = Food, ["FRESHMARKET"] = Food,
            ["PIEKARNIA"] = Food, ["MCDONALDS"] = Food, ["KFC"] = Food, ["PIZZA"] = Food,
            ["STARBUCKS"] = Food, ["COSTA"] = Food, ["GLOVO"] = Food, ["PYSZNE"] = Food,
            ["UBEREATS"] = Food, ["BOLTFOOD"] = Food, ["SUBWAY"] = Food, ["SALAD"] = Food,

            // Транспорт
            ["ORLEN"] = Transport, ["SHELL"] = Transport, ["CIRCLE"] = Transport,
            ["LOTOS"] = Transport, ["AMIC"] = Transport, ["MOYA"] = Transport,
            ["MPK"] = Transport, ["ZTM"] = Transport, ["JAKDOJADE"] = Transport,
            ["UBER"] = Transport, ["BOLT"] = Transport, ["FREENOW"] = Transport,
            ["PKP"] = Transport, ["INTERCITY"] = Transport, ["FLIXBUS"] = Transport,
            ["RYANAIR"] = Transport, ["WIZZAIR"] = Transport, ["LOT"] = Transport,
            ["AUTOPAY"] = Transport, ["PARKING"] = Transport,

            // Здоров'я
            ["APTEKA"] = Health, ["ROSSMANN"] = Health, ["HEBE"] = Health,
            ["SUPERPHARM"] = Health, ["DOZ"] = Health, ["GEMINI"] = Health,
            ["LUXMED"] = Health, ["MEDICOVER"] = Health, ["ENELMED"] = Health,
            ["DENTAL"] = Health, ["MULTISPORT"] = Health, ["MEDIQ"] = Health,

            // Житло й рахунки
            ["TAURON"] = Home, ["PGE"] = Home, ["ENEA"] = Home, ["ENERGA"] = Home,
            ["PGNIG"] = Home, ["VEOLIA"] = Home, ["WODOCIAGI"] = Home,
            ["ORANGE"] = Home, ["PLAY"] = Home, ["PLUS"] = Home, ["TMOBILE"] = Home,
            ["UPC"] = Home, ["VECTRA"] = Home, ["NETIA"] = Home, ["CZYNSZ"] = Home,
            ["IKEA"] = Home, ["LEROY"] = Home, ["CASTORAMA"] = Home, ["JYSK"] = Home,
            ["OBI"] = Home,

            // Розваги
            ["NETFLIX"] = Fun, ["SPOTIFY"] = Fun, ["STEAM"] = Fun, ["PLAYSTATION"] = Fun,
            ["XBOX"] = Fun, ["HBO"] = Fun, ["DISNEY"] = Fun, ["CINEMA"] = Fun,
            ["MULTIKINO"] = Fun, ["HELIOS"] = Fun, ["EMPIK"] = Fun, ["YOUTUBE"] = Fun,
            ["PATREON"] = Fun, ["TWITCH"] = Fun,
        };

    // The names the app seeds its categories with. Kept as constants so a typo cannot
    // silently produce a rule that matches no category and quietly does nothing.
    public const string Food = "Їжа";
    public const string Transport = "Транспорт";
    public const string Health = "Здоров'я";
    public const string Home = "Житло";
    public const string Fun = "Розваги";

    /// <summary>The category name for a merchant key, or null when it is not a shop we know.</summary>
    public static string? CategoryNameFor(string key) =>
        key.Length > 0 && ByKey.TryGetValue(key, out var name) ? name : null;
}
