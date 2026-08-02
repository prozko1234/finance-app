namespace FinanceApp.Domain;

/// One-off vs recurring expense.
public enum Frequency { OneOff, Recurring }

/// How often a recurring charge comes back. Paired with an interval ("every 2 weeks",
/// "every 3 months"), which is what covers quarterly and half-yearly without inventing
/// units for them.
///
/// No Day: a daily charge is not a subscription, it is a habit, and materializing one would
/// bury the ledger it is supposed to explain.
public enum RecurrenceUnit { Week, Month, Year }

/// Where the transaction came from. Recurring = auto-generated from a RecurringExpense.
public enum TxSource { Manual, Bank, Notification, Recurring }

/// Money out (Expense) or money in (Income). Income rows carry the VAT split
/// and count towards the monthly revenue that taxes are computed on.
public enum TransactionKind { Expense, Income }
