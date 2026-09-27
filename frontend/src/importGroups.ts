import type { ImportRow, ImportRowToSave, RecurringMatch } from './types'

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
  /// Treat this shop's rows as a subscription's charge rather than as new expenses. Pre-filled
  /// from the server's match; set by hand for anything it could not recognise.
  recurringId: number | null
  /// Also set the subscription's price from the row. Only offered in one currency.
  updateAmount: boolean
  /// What the server made of this shop, when it made anything. Kept so the row can show the
  /// two amounts and say whether the match is a fact or a guess.
  match: RecurringMatch | null
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

    const match = row.recurring ?? null
    byKey.set(key, {
      key,
      merchant: row.merchant || row.description || '—',
      rows: [row],
      total: row.amount,
      categoryId: row.suggestedCategoryId,
      include: true,
      // A learned match is a fact, so it is acted on. A guess is not: the app matched on the
      // numbers alone, and linking the wrong subscription would confirm a bill that never
      // arrived while the real one stays unpaid.
      recurringId: match?.learned ? match.recurringId : null,
      updateAmount: false,
      match,
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
  return groups.filter((g) => g.include && g.categoryId === null && g.recurringId === null).length
}

/// The rows that go to the server: enabled groups only, each row carrying its group's
/// category. Groups without a category drop out — the server would refuse them anyway, and
/// silently losing half an import would look like success.
export function rowsToCommit(
  groups: ImportGroup[], extra: ImportRow[] = [], extraCategoryId: number | null = null,
): ImportRowToSave[] {
  // A group linked to a subscription needs no category: nothing is created for it, so the
  // usual "no category, no import" rule would drop exactly the rows that matter.
  const fromGroups = groups
    .filter((g) => g.include && (g.categoryId !== null || g.recurringId !== null))
    .flatMap((g) => g.rows.map((row) => ({
      row,
      categoryId: g.categoryId,
      recurringId: g.recurringId,
      updateAmount: g.updateAmount,
    })))

  const fromExtra = extraCategoryId === null
    ? []
    : extra.map((row) => ({
      row, categoryId: extraCategoryId, recurringId: null, updateAmount: false,
    }))

  return [...fromGroups, ...fromExtra].map(({ row, categoryId, recurringId, updateAmount }) => ({
    line: row.line,
    date: row.date,
    amount: row.amount,
    currency: row.currency,
    // The server ignores it for a linked row, but the contract wants one and a missing value
    // would be a trap for whoever reads the payload next.
    categoryId: categoryId ?? row.suggestedCategoryId ?? 0,
    note: row.description,
    ...(recurringId === null ? {} : { recurringId, updateRecurringAmount: updateAmount }),
  }))
}

/// Groups still waiting on the one question that cannot be guessed: a shop the server matched
/// to a subscription only by its numbers. Acting on it unasked would confirm a bill that never
/// arrived and leave the real one unpaid.
export function unansweredGuesses(groups: ImportGroup[]): number {
  return groups.filter((g) => g.include && g.match && !g.match.learned && g.recurringId === null).length
}
