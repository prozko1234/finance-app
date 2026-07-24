namespace FinanceApp.Domain;

/// How necessary the expense is (for future safe-to-spend and analytics).
public enum Priority { Must, Should, Want }

/// One-off vs recurring expense.
public enum Frequency { OneOff, Recurring }

/// Where the transaction came from. Phase 1 — Manual only.
public enum TxSource { Manual, Bank, Notification }
