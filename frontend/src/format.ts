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

/// Українська множина: 1 запис, 2 записи, 5 записів, 21 запис.
///
/// Потрібна саме форма, а не «1 записів»: список, який рахує сам себе неправильно, читається
/// як недороблений, і це помічають раніше за будь-яку іншу дрібницю.
export function plural(n: number, one: string, few: string, many: string): string {
  const abs = Math.abs(n) % 100
  // 11–14 — виняток: там завжди «багато», хоч остання цифра й підказує інше.
  if (abs >= 11 && abs <= 14) return many

  const last = abs % 10
  if (last === 1) return one
  if (last >= 2 && last <= 4) return few
  return many
}

/// Сума транзакції так, як її читають: дохід із плюсом, витрата з мінусом.
///
/// Мінус — саме типографський «−» (U+2212), а не дефіс: він тієї ж ширини, що й цифри,
/// тож стовпчик сум не роз'їжджається на один піксель у кожному рядку.
export function signedMoney(amount: number, currency: string, kind: 'Income' | 'Expense'): string {
  return `${kind === 'Income' ? '+' : '−'}${money(Math.abs(amount), currency)}`
}

/// Колір тієї ж суми. Зелений — прихід, червоний — витрата.
export function signedMoneyClass(kind: 'Income' | 'Expense'): string {
  return kind === 'Income' ? 'text-emerald-600' : 'text-red-600'
}
