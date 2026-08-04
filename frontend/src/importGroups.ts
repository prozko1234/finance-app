import type { ImportRow } from './types'

/// One shop in the preview: all of its rows, together.
///
/// This is the main reason a 300-row import is not 300 decisions. A month's statement is
/// usually 20–30 different shops, and "ŻABKA × 14 · 340 zł" as one row with one category
/// choice leaves exactly as much work as there actually is.
export interface ImportGroup {
  key: string
  merchant: string
  rows: ImportRow[]
  /// The group's total, signed: expenses negative, income positive.
  total: number
  /// Where it goes. Null — nobody knows, and those groups are the ones to show first.
  categoryId: number | null
  include: boolean
}

/// The groups ready to be shown. Duplicates stay out — they get their own list, off by
/// default: a row the app already has must not quietly be added a second time.
export function groupRows(rows: ImportRow[]): ImportGroup[] {
  const byKey = new Map<string, ImportGroup>()

  for (const row of rows) {
    if (row.duplicateOfId !== null) continue

    // A row with no shop name has nothing to group by, so each stands alone — otherwise
    // nameless transfers from different months would clump into one pile.
    const key = row.merchantKey || `line:${row.line}`
    const existing = byKey.get(key)

    if (existing) {
      existing.rows.push(row)
      existing.total += row.amount
      continue
    }

    byKey.set(key, {
      key,
      merchant: row.merchant || row.description || '—',
      rows: [row],
      total: row.amount,
      categoryId: row.suggestedCategoryId,
      include: true,
    })
  }

  // Unknown ones first: they are the only ones needing a decision. Then by size, because that
  // is where a wrong category costs the most.
  return [...byKey.values()].sort((a, b) => {
    if ((a.categoryId === null) !== (b.categoryId === null)) return a.categoryId === null ? -1 : 1
    return Math.abs(b.total) - Math.abs(a.total)
  })
}

/// How many groups are still waiting for a decision. The import button does not care — it can
/// go ahead regardless — but it is worth saying.
export function undecidedCount(groups: ImportGroup[]): number {
  return groups.filter((g) => g.include && g.categoryId === null).length
}

/// The rows that go to the server: enabled groups only, each row carrying its group's
/// category. Groups without a category drop out — the server would refuse them anyway, and
/// silently losing half an import would look like success.
export function rowsToCommit(groups: ImportGroup[], extra: ImportRow[] = [], extraCategoryId: number | null = null) {
  const fromGroups = groups
    .filter((g) => g.include && g.categoryId !== null)
    .flatMap((g) => g.rows.map((r) => ({ row: r, categoryId: g.categoryId! })))

  const fromExtra = extraCategoryId === null
    ? []
    : extra.map((r) => ({ row: r, categoryId: extraCategoryId }))

  return [...fromGroups, ...fromExtra].map(({ row, categoryId }) => ({
    line: row.line,
    date: row.date,
    amount: row.amount,
    currency: row.currency,
    categoryId,
    note: row.description,
  }))
}
