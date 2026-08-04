/// "20 липня" — day and month, no year. The year in these labels is always the current one,
/// and showing it would add noise to a sentence meant to be read in half a second.
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

/// How many days are left until a date. Counted by local midnight rather than by hours: "за 6
/// днів" must not become "за 5" just because it is evening.
export function daysUntil(iso: string): number {
  const [y, m, d] = iso.split('-').map(Number)
  const target = new Date(y, m - 1, d)
  const now = new Date()
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  return Math.round((target.getTime() - today.getTime()) / 86_400_000)
}

/// A group heading in the recent list: "Сьогодні", "Вчора" or "28 липня". The date used to
/// repeat on every row and added nothing — a group needs it once.
export function dayHeading(iso: string, today = new Date()): string {
  const midnight = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  const [y, m, d] = iso.split('-').map(Number)
  const days = Math.round((new Date(y, m - 1, d).getTime() - midnight.getTime()) / 86_400_000)
  if (days === 0) return 'Сьогодні'
  if (days === -1) return 'Вчора'
  return dayMonth(iso)
}

/// Ukrainian plurals: 1 запис, 2 записи, 5 записів, 21 запис.
///
/// The right form matters: a list that miscounts itself reads as unfinished, and it is noticed
/// before any other small thing.
export function plural(n: number, one: string, few: string, many: string): string {
  const abs = Math.abs(n) % 100
  // 11–14 are the exception: always the many-form, whatever the last digit suggests.
  if (abs >= 11 && abs <= 14) return many

  const last = abs % 10
  if (last === 1) return one
  if (last >= 2 && last <= 4) return few
  return many
}

/// A transaction's amount as it is read: income with a plus, an expense with a minus.
///
/// The minus is the typographic one (U+2212), not a hyphen: it is the same width as the
/// digits, so a column of amounts does not drift by a pixel per row.
export function signedMoney(amount: number, currency: string, kind: 'Income' | 'Expense'): string {
  return `${kind === 'Income' ? '+' : '−'}${money(Math.abs(amount), currency)}`
}

/// The same amount's colour. Green for money in, red for money out.
export function signedMoneyClass(kind: 'Income' | 'Expense'): string {
  return kind === 'Income' ? 'text-emerald-600' : 'text-red-600'
}
