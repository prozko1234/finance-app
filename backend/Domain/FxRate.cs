namespace FinanceApp.Domain;

/// Кеш курсів: скільки PLN за 1 одиницю валюти на запитану дату.
/// Щоб не смикати зовнішнє API повторно для тієї ж пари (валюта, дата).
public class FxRate
{
    public required string Currency { get; set; }   // ISO-код, напр. USD
    public DateOnly Date { get; set; }               // запитана дата транзакції
    public decimal PlnPerUnit { get; set; }
    public DateOnly EffectiveDate { get; set; }      // фактична дата курсу (може бути раніше)
    public required string Source { get; set; }      // NBP / ECB
    public DateTimeOffset FetchedAt { get; set; }
}
