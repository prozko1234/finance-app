export function money(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('pl-PL', { style: 'currency', currency }).format(amount)
  } catch {
    // Unknown ISO code — show the number + code.
    return `${amount.toFixed(2)} ${currency}`
  }
}
