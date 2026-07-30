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

/// Скільки днів лишилось до дати. Рахується по локальній півночі, а не по годинах: «за 6 днів»
/// не має ставати «за 5», бо зараз вечір.
export function daysUntil(iso: string): number {
  const [y, m, d] = iso.split('-').map(Number)
  const target = new Date(y, m - 1, d)
  const now = new Date()
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  return Math.round((target.getTime() - today.getTime()) / 86_400_000)
}

/// Заголовок групи в списку останніх: «Сьогодні», «Вчора» або «28 липня». Дата рядком
/// повторювалась у кожному записі й нічого не додавала — у групі вона потрібна один раз.
export function dayHeading(iso: string, today = new Date()): string {
  const midnight = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  const [y, m, d] = iso.split('-').map(Number)
  const days = Math.round((new Date(y, m - 1, d).getTime() - midnight.getTime()) / 86_400_000)
  if (days === 0) return 'Сьогодні'
  if (days === -1) return 'Вчора'
  return dayMonth(iso)
}
