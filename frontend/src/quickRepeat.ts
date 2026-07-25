import type { Transaction } from './types'

export interface QuickAction {
  key: string
  categoryId: number
  categoryName: string
  icon?: string | null
  amount: number
  currency: string
}

/// Turns recent expenses into one-tap repeat buttons.
/// Same category+amount+currency collapses into a single action, most recent first —
/// so the everyday "coffee again" costs exactly one tap.
export function buildQuickActions(
  transactions: Transaction[],
  iconFor: (categoryName: string) => string | null,
  limit = 3,
): QuickAction[] {
  const seen = new Map<string, QuickAction>()

  for (const t of transactions) {
    if (t.kind !== 'Expense') continue
    const key = `${t.categoryId}:${t.amountOriginal}:${t.currencyOriginal}`
    if (seen.has(key)) continue

    seen.set(key, {
      key,
      categoryId: t.categoryId,
      categoryName: t.categoryName,
      icon: iconFor(t.categoryName),
      amount: t.amountOriginal,
      currency: t.currencyOriginal,
    })
    if (seen.size >= limit) break
  }

  return [...seen.values()]
}
