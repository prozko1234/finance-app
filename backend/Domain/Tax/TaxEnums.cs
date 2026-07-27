namespace FinanceApp.Domain.Tax;

/// How the user's income is taxed. Not everyone runs a business: None is the default
/// for a new profile, so the app is usable before any tax setup exists.
/// Stored as a string, so adding or reordering members is schema-safe.
public enum TaxRegime
{
    /// "Just money" — what you entered is entirely yours. No VAT, no contributions.
    None,
    /// Polish B2B lump-sum tax.
    Ryczalt,
    /// Umowa o pracę — employment contract, gross from the contract to net.
    UoP,
    /// Umowa zlecenie — mandate contract.
    Zlecenie,
}

/// ZUS social contribution scheme. Amounts are stored on the profile (editable),
/// this only records which scheme the user is on.
public enum ZusType { Duzy, Preferencyjny, UlgaNaStart, MalyZusPlus }
