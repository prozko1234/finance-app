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
            // Groceries
            ["BIEDRONKA"] = Groceries, ["JMP"] = Groceries, ["LIDL"] = Groceries,
            ["ZABKA"] = Groceries, ["ŻABKA"] = Groceries, ["AUCHAN"] = Groceries,
            ["CARREFOUR"] = Groceries, ["KAUFLAND"] = Groceries, ["NETTO"] = Groceries,
            ["ALDI"] = Groceries, ["DINO"] = Groceries, ["STOKROTKA"] = Groceries,
            ["FRESHMARKET"] = Groceries, ["DELIKATESY"] = Groceries, ["PIEKARNIA"] = Groceries,
            ["SELGROS"] = Groceries, ["MAKRO"] = Groceries,

            // Delivery, deliberately apart from groceries: over a year it turned out to be
            // twice the line item, and merged into groceries it would have been invisible.
            ["GLOVO"] = Delivery, ["PYSZNE"] = Delivery, ["UBEREATS"] = Delivery,
            ["BOLTFOOD"] = Delivery, ["WOLT"] = Delivery, ["MACZFIT"] = Delivery,
            ["LITEBOX"] = Delivery, ["NICELUNCH"] = Delivery,

            // Cafés and bars
            ["MCDONALDS"] = EatingOut, ["KFC"] = EatingOut, ["BURGER"] = EatingOut,
            ["PIZZA"] = EatingOut, ["STARBUCKS"] = EatingOut, ["COSTA"] = EatingOut,
            ["SUBWAY"] = EatingOut, ["PUB"] = EatingOut, ["JAMESON"] = EatingOut,
            ["KAWIARNIA"] = EatingOut, ["RESTAURACJA"] = EatingOut, ["BISTRO"] = EatingOut,

            // Transport
            ["ORLEN"] = Transport, ["SHELL"] = Transport, ["CIRCLE"] = Transport,
            ["LOTOS"] = Transport, ["AMIC"] = Transport, ["MOYA"] = Transport,
            ["MPK"] = Transport, ["ZTM"] = Transport, ["JAKDOJADE"] = Transport,
            ["UBER"] = Transport, ["BOLT"] = Transport, ["FREENOW"] = Transport,
            ["PKP"] = Transport, ["INTERCITY"] = Transport, ["FLIXBUS"] = Transport,
            ["RYANAIR"] = Transport, ["WIZZAIR"] = Transport, ["AUTOPAY"] = Transport,
            ["PARKING"] = Transport,

            // Health
            ["APTEKA"] = Health, ["ROSSMANN"] = Health, ["HEBE"] = Health,
            ["SUPERPHARM"] = Health, ["DOZ"] = Health, ["GEMINI"] = Health,
            ["LUXMED"] = Health, ["EMARKET"] = Health, ["MEDICOVER"] = Health,
            ["ENELMED"] = Health, ["STOMAT"] = Health, ["DENTAL"] = Health,
            ["MULTISPORT"] = Health, ["MYBENEFIT"] = Health, ["MEDIQ"] = Health,

            // Housing and bills
            ["TAURON"] = Home, ["PGE"] = Home, ["ENEA"] = Home, ["ENERGA"] = Home,
            ["PGNIG"] = Home, ["VEOLIA"] = Home, ["WODOCIAGI"] = Home, ["CZYNSZ"] = Home,
            ["IKEA"] = Home, ["LEROY"] = Home, ["CASTORAMA"] = Home, ["JYSK"] = Home,
            ["OBI"] = Home,

            // Subscriptions — the things cancelled in one move. Kept apart from entertainment
            // for exactly that reason: they are different decisions and should look different.
            ["NETFLIX"] = Subscriptions, ["SPOTIFY"] = Subscriptions, ["HBO"] = Subscriptions,
            ["DISNEY"] = Subscriptions, ["YOUTUBE"] = Subscriptions, ["ICLOUD"] = Subscriptions,
            ["APPLE"] = Subscriptions, ["GOOGLE"] = Subscriptions, ["MICROSOFT"] = Subscriptions,
            ["OPENAI"] = Subscriptions, ["ANTHROPIC"] = Subscriptions, ["GITHUB"] = Subscriptions,
            ["PATREON"] = Subscriptions, ["ORANGE"] = Subscriptions, ["PLAY"] = Subscriptions,
            ["PLUS"] = Subscriptions, ["TMOBILE"] = Subscriptions, ["UPC"] = Subscriptions,
            ["VECTRA"] = Subscriptions, ["NETIA"] = Subscriptions,

            // Entertainment
            ["STEAM"] = Fun, ["STEAMGAMES"] = Fun, ["PLAYSTATION"] = Fun, ["XBOX"] = Fun,
            ["NINTENDO"] = Fun, ["CINEMA"] = Fun, ["MULTIKINO"] = Fun, ["HELIOS"] = Fun,
            ["EMPIK"] = Fun, ["TWITCH"] = Fun,

            // Transfers to people and to oneself. Not an expense in the strict sense — but
            // 12,866 zł over a year, and without a line of their own they disappeared into
            // "Інше", which then explained nothing.
            ["REVOLUT"] = Transfers, ["PRZELEWU"] = Transfers, ["PRZELEW"] = Transfers,
            ["BLIK"] = Transfers, ["PAYPAL"] = Transfers,

            // Added from a real year of statements: what actually happened and was missing
            // from a "first week in Poland" list. Only what anyone would recognise goes in —
            // chains and generic words; specific people and local places stay out, because
            // they are a fact about one person rather than about the country.
            ["SPOLEM"] = Groceries, ["ELECLERC"] = Groceries, ["KONZUM"] = Groceries,
            ["WYPIEKARNIA"] = Groceries,

            // Generic words in a venue's name. Chains do not help here: "BAR NA RÓWNI" and
            // "DARA KEBAB" are different places, but neither is a grocery shop.
            ["PIJALNIA"] = EatingOut, ["PIWIARNIA"] = EatingOut, ["KEBAB"] = EatingOut,
            ["RAMEN"] = EatingOut, ["BARISTA"] = EatingOut, ["KONOBA"] = EatingOut,
            ["RESTORAN"] = EatingOut, ["POPEYES"] = EatingOut, ["CAFE"] = EatingOut,
            ["BAR"] = EatingOut, ["PIZZERIA"] = EatingOut,

            ["KOLEO"] = Transport, ["APCOA"] = Transport, ["CARWASH"] = Transport,
            ["MYJNIA"] = Transport, ["INPOST"] = Transport,

            ["ENERGYLANDIA"] = Fun, ["CYBERMACHINA"] = Fun, ["ENEBA"] = Fun,
            ["RIOTGAMES"] = Fun, ["MIDASBUY"] = Fun, ["XSOLLA"] = Fun,
            ["STRZELNICA"] = Fun, ["TERMY"] = Fun,

            ["BARBERSHOP"] = Health, ["ZDROFIT"] = Health, ["KAFETERIA"] = Health,
            ["SEPHORA"] = Health,

            // Phone top-ups and hosting: the same monthly commitment as a mobile operator.
            ["DOŁADOWANIE"] = Subscriptions, ["DOLADOWANIE"] = Subscriptions,
            ["SCALACUBE"] = Subscriptions,
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
