namespace FinanceApp.Domain;

/// How necessary the expense is (for future safe-to-spend and analytics).
public enum Priority { Must, Should, Want }

/// One-off vs recurring expense.
public enum Frequency { OneOff, Recurring }

/// Where the transaction came from. Recurring = auto-generated from a RecurringExpense.
public enum TxSource { Manual, Bank, Notification, Recurring }
