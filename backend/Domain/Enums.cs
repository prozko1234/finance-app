namespace FinanceApp.Domain;

/// Наскільки витрата обов'язкова (для майбутнього safe-to-spend і аналітики).
public enum Priority { Must, Should, Want }

/// Разова чи повторювана витрата.
public enum Frequency { OneOff, Recurring }

/// Звідки з'явилась транзакція. У фазі 1 — лише Manual.
public enum TxSource { Manual, Bank, Notification }
