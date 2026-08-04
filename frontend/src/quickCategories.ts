import type { Transaction } from './types'

export interface QuickCategory {
  categoryId: number
  categoryName: string
  icon?: string | null
  uses: number
}

/// Turns recent expenses into one-tap category shortcuts.
///
/// Deliberately NOT a repeat of an exact past amount: the same category comes back
/// constantly, the same amount almost never does. Guessing the amount would put a wrong
/// number in front of the user and make them delete it — worse than typing it.
/// So a tap picks the category and leaves the amount to be entered.
///
/// Ranked by how often the category is used, ties broken by whichever was used most
/// recently, so the order is stable rather than jumping around after every entry.
export function buildQuickCategories(
  transactions: Transaction[],
  limit = 4,
): QuickCategory[] {
  const counts = new Map<number, QuickCategory & { lastSeen: number }>()

  transactions.forEach((t, index) => {
    if (t.kind !== 'Expense') return

    const existing = counts.get(t.categoryId)
    if (existing) {
      existing.uses += 1
      return
    }

    counts.set(t.categoryId, {
      categoryId: t.categoryId,
      categoryName: t.categoryName,
      icon: t.categoryIcon ?? null,
      uses: 1,
      lastSeen: index, // transactions arrive newest first, so smaller = more recent
    })
  })

  return [...counts.values()]
    .sort((a, b) => b.uses - a.uses || a.lastSeen - b.lastSeen)
    .slice(0, limit)
    .map(({ lastSeen: _lastSeen, ...c }) => c)
}
