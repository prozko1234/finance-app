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
            ["BIEDRONKA"] = Groceries, ["JMP"] = Groceries, ["LIDL"] = Groceries,
            ["ZABKA"] = Groceries, ["ŻABKA"] = Groceries, ["AUCHAN"] = Groceries,
            ["CARREFOUR"] = Groceries, ["KAUFLAND"] = Groceries, ["NETTO"] = Groceries,
            ["ALDI"] = Groceries, ["DINO"] = Groceries, ["STOKROTKA"] = Groceries,
            ["FRESHMARKET"] = Groceries, ["DELIKATESY"] = Groceries, ["PIEKARNIA"] = Groceries,
            ["SELGROS"] = Groceries, ["MAKRO"] = Groceries,

            // Доставка — окремо від продуктів навмисно: за рік це виявилось удвічі більшою
            // статтею, і злите з продуктами воно було б невидимим.
            ["GLOVO"] = Delivery, ["PYSZNE"] = Delivery, ["UBEREATS"] = Delivery,
            ["BOLTFOOD"] = Delivery, ["WOLT"] = Delivery, ["MACZFIT"] = Delivery,
            ["LITEBOX"] = Delivery, ["NICELUNCH"] = Delivery,

            // Кафе й бари
            ["MCDONALDS"] = EatingOut, ["KFC"] = EatingOut, ["BURGER"] = EatingOut,
            ["PIZZA"] = EatingOut, ["STARBUCKS"] = EatingOut, ["COSTA"] = EatingOut,
            ["SUBWAY"] = EatingOut, ["PUB"] = EatingOut, ["JAMESON"] = EatingOut,
            ["KAWIARNIA"] = EatingOut, ["RESTAURACJA"] = EatingOut, ["BISTRO"] = EatingOut,

            // Транспорт
            ["ORLEN"] = Transport, ["SHELL"] = Transport, ["CIRCLE"] = Transport,
            ["LOTOS"] = Transport, ["AMIC"] = Transport, ["MOYA"] = Transport,
            ["MPK"] = Transport, ["ZTM"] = Transport, ["JAKDOJADE"] = Transport,
            ["UBER"] = Transport, ["BOLT"] = Transport, ["FREENOW"] = Transport,
            ["PKP"] = Transport, ["INTERCITY"] = Transport, ["FLIXBUS"] = Transport,
            ["RYANAIR"] = Transport, ["WIZZAIR"] = Transport, ["AUTOPAY"] = Transport,
            ["PARKING"] = Transport,

            // Здоров'я
            ["APTEKA"] = Health, ["ROSSMANN"] = Health, ["HEBE"] = Health,
            ["SUPERPHARM"] = Health, ["DOZ"] = Health, ["GEMINI"] = Health,
            ["LUXMED"] = Health, ["EMARKET"] = Health, ["MEDICOVER"] = Health,
            ["ENELMED"] = Health, ["STOMAT"] = Health, ["DENTAL"] = Health,
            ["MULTISPORT"] = Health, ["MYBENEFIT"] = Health, ["MEDIQ"] = Health,

            // Житло й рахунки
            ["TAURON"] = Home, ["PGE"] = Home, ["ENEA"] = Home, ["ENERGA"] = Home,
            ["PGNIG"] = Home, ["VEOLIA"] = Home, ["WODOCIAGI"] = Home, ["CZYNSZ"] = Home,
            ["IKEA"] = Home, ["LEROY"] = Home, ["CASTORAMA"] = Home, ["JYSK"] = Home,
            ["OBI"] = Home,

            // Підписки — те, що скасовується одним рухом. Відділено від розваг саме тому:
            // це різні рішення, і в списку вони мають виглядати по-різному.
            ["NETFLIX"] = Subscriptions, ["SPOTIFY"] = Subscriptions, ["HBO"] = Subscriptions,
            ["DISNEY"] = Subscriptions, ["YOUTUBE"] = Subscriptions, ["ICLOUD"] = Subscriptions,
            ["APPLE"] = Subscriptions, ["GOOGLE"] = Subscriptions, ["MICROSOFT"] = Subscriptions,
            ["OPENAI"] = Subscriptions, ["ANTHROPIC"] = Subscriptions, ["GITHUB"] = Subscriptions,
            ["PATREON"] = Subscriptions, ["ORANGE"] = Subscriptions, ["PLAY"] = Subscriptions,
            ["PLUS"] = Subscriptions, ["TMOBILE"] = Subscriptions, ["UPC"] = Subscriptions,
            ["VECTRA"] = Subscriptions, ["NETIA"] = Subscriptions,

            // Розваги
            ["STEAM"] = Fun, ["STEAMGAMES"] = Fun, ["PLAYSTATION"] = Fun, ["XBOX"] = Fun,
            ["NINTENDO"] = Fun, ["CINEMA"] = Fun, ["MULTIKINO"] = Fun, ["HELIOS"] = Fun,
            ["EMPIK"] = Fun, ["TWITCH"] = Fun,

            // Перекази людям і собі. Не витрата у прямому сенсі — але за рік це 12 866 zł,
            // і без власної статті вони губились у «Інше», яке тоді нічого не пояснювало.
            ["REVOLUT"] = Transfers, ["PRZELEWU"] = Transfers, ["PRZELEW"] = Transfers,
            ["BLIK"] = Transfers, ["PAYPAL"] = Transfers,
        };

    // The names the app seeds its categories with. Constants so a typo cannot silently
    // produce a rule that matches no category and quietly does nothing.
    public const string Groceries = "Продукти";
    public const string Delivery = "Доставка";
    public const string EatingOut = "Кафе й бари";
    public const string Transport = "Транспорт";
    public const string Health = "Здоров'я";
    public const string Home = "Житло";
    public const string Subscriptions = "Підписки";
    public const string Fun = "Розваги";
    public const string Transfers = "Перекази";

    /// <summary>The category name for a merchant key, or null when it is not a shop we know.</summary>
    public static string? CategoryNameFor(string key) =>
        key.Length > 0 && ByKey.TryGetValue(key, out var name) ? name : null;
}
