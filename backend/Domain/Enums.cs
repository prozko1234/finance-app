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

/// Whether the money has actually moved. Anything the user types is Posted the moment it is
/// written — they were standing at the till. A recurring charge is Pending until they say it
/// went through: the app knows the schedule, not the bank, and «я ще не оплатив, а воно вже
/// рахує» is exactly what happens when a calendar is read as a receipt.
///
/// Pending money is not free money. It stays reserved out of the daily norm — confirming it
/// moves it from one column to another and changes nothing the user can spend.
public enum TxStatus { Posted, Pending }
