namespace FinanceApp.Domain;

/// How necessary the expense is (for future safe-to-spend and analytics).
public enum Priority { Must, Should, Want }

/// One-off vs recurring expense.
public enum Frequency { OneOff, Recurring }

/// Where the transaction came from. Recurring = auto-generated from a RecurringExpense.
public enum TxSource { Manual, Bank, Notification, Recurring }

/// Money out (Expense) or money in (Income). Income rows carry the VAT split
/// and count towards the monthly revenue that taxes are computed on.
public enum TransactionKind { Expense, Income }
