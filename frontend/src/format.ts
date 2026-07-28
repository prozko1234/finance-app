/// «20 липня» — день і місяць без року. Рік у цих підписах завжди поточний, і показувати
/// його означало б додати шуму в речення, яке має читатись за пів секунди.
export function dayMonth(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return new Intl.DateTimeFormat('uk-UA', { day: 'numeric', month: 'long' }).format(d)
}

export function money(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('pl-PL', { style: 'currency', currency }).format(amount)
  } catch {
    // Unknown ISO code — show the number + code.
    return `${amount.toFixed(2)} ${currency}`
  }
}
