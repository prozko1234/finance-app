export function money(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('pl-PL', { style: 'currency', currency }).format(amount)
  } catch {
    // Невідомий ISO-код — показуємо число + код.
    return `${amount.toFixed(2)} ${currency}`
  }
}
