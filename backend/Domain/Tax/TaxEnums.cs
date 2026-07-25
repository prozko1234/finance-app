namespace FinanceApp.Domain.Tax;

/// Polish B2B taxation form. Phase 1 computes Ryczalt only; others are declared
/// so the profile can store them, but calculation returns Unsupported for now.
public enum TaxRegime { Ryczalt, Liniowy, Skala }

/// ZUS social contribution scheme. Amounts are stored on the profile (editable),
/// this only records which scheme the user is on.
public enum ZusType { Duzy, Preferencyjny, UlgaNaStart, MalyZusPlus }
