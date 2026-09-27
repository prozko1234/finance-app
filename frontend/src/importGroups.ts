import type { ImportRow, ImportRowToSave } from './types'

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
    // A subscription's charge is not a shop to file: the app is already holding that money,
    // and the only question is whether this row IS that charge. Asked one row at a time in
    // its own section, because the answer changes a subscription rather than a category.
    if (row.recurring) continue

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

/// What to do with a statement row that looks like a subscription's charge.
///
/// 'reprice' also confirms — correcting the price and then leaving the charge unanswered would
/// be half an answer. 'expense' drops the row back among the ordinary ones.
export type SubscriptionChoice = 'confirm' | 'reprice' | 'expense'

export interface SubscriptionDecision {
  row: ImportRow
  choice: SubscriptionChoice | null
}

/// The subscription rows, with the answer the app is willing to assume.
///
/// Nothing is assumed when the match was a GUESS: the app matched on the date alone, and
/// silently linking a row to the wrong subscription would confirm a bill that never arrived
/// and leave the real one unpaid. A learned match is a fact and needs no ceremony.
export function subscriptionDecisions(rows: ImportRow[]): SubscriptionDecision[] {
  return rows
    .filter((r) => r.duplicateOfId === null && r.recurring)
    .map((row) => {
      const m = row.recurring!
      if (!m.learned) return { row, choice: null }

      const differs = Math.abs(Math.abs(row.amount) - m.ruleAmount) >= 0.01
      return { row, choice: (differs && m.canUpdateAmount ? 'reprice' : 'confirm') as SubscriptionChoice }
    })
}

/// The subscription rows as the server wants them. 'expense' ones are left out — they belong
/// to the ordinary groups instead.
export function subscriptionRowsToCommit(
  decisions: SubscriptionDecision[], fallbackCategoryId: number | null,
): ImportRowToSave[] {
  return decisions
    .filter((d) => d.choice === 'confirm' || d.choice === 'reprice')
    .map((d) => ({
      line: d.row.line,
      date: d.row.date,
      amount: d.row.amount,
      currency: d.row.currency,
      // The server ignores it for a linked row, but the contract wants one and a nonsense
      // value would be a trap for whoever reads the payload next.
      categoryId: d.row.suggestedCategoryId ?? fallbackCategoryId ?? 0,
      note: d.row.description,
      recurringId: d.row.recurring!.recurringId,
      updateRecurringAmount: d.choice === 'reprice',
    }))
}

/// Rows the user sent back to the ordinary pile, so the groups can pick them up.
///
/// The match is STRIPPED, not merely ignored: groupRows skips anything still carrying one, so
/// a row handed back with its match attached would be filtered out of both lists and vanish
/// from the import without a word.
export function rowsAsPlainExpenses(decisions: SubscriptionDecision[]): ImportRow[] {
  return decisions
    .filter((d) => d.choice === 'expense')
    .map(({ row }) => ({ ...row, recurring: null }))
}

/// How many groups are still waiting for a decision. The import button does not care — it can
/// go ahead regardless — but it is worth saying.
export function undecidedCount(groups: ImportGroup[]): number {
  return groups.filter((g) => g.include && g.categoryId === null).length
}

/// The rows that go to the server: enabled groups only, each row carrying its group's
/// category. Groups without a category drop out — the server would refuse them anyway, and
/// silently losing half an import would look like success.
export function rowsToCommit(
  groups: ImportGroup[], extra: ImportRow[] = [], extraCategoryId: number | null = null,
): ImportRowToSave[] {
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
