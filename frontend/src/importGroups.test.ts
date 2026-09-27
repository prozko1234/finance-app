import { describe, expect, it } from 'vitest'
import {
  groupRows, rowsAsPlainExpenses, rowsToCommit, subscriptionDecisions,
  subscriptionRowsToCommit, undecidedCount,
} from './importGroups'
import type { ImportRow, RecurringMatch } from './types'

function row(over: Partial<ImportRow> = {}): ImportRow {
  return {
    line: 1, date: '2026-08-01', amount: -10, currency: 'PLN',
    description: 'ZABKA Z1234', merchant: 'ZABKA', merchantKey: 'ZABKA',
    kind: 'Expense', duplicateOfId: null, suggestedCategoryId: 1,
    ...over,
  }
}

describe('groupRows', () => {
  it('puts every visit to one shop into a single decision', () => {
    const groups = groupRows([
      row({ line: 1, amount: -12 }),
      row({ line: 2, amount: -18 }),
      row({ line: 3, amount: -40, merchantKey: 'LIDL', merchant: 'LIDL' }),
    ])

    expect(groups).toHaveLength(2)
    expect(groups.find((g) => g.key === 'ZABKA')!.rows).toHaveLength(2)
    expect(groups.find((g) => g.key === 'ZABKA')!.total).toBe(-30)
  })

  it('leaves duplicates out — a row already in the app must not slip back in', () => {
    const groups = groupRows([row({ line: 1 }), row({ line: 2, duplicateOfId: 77 })])

    expect(groups).toHaveLength(1)
    expect(groups[0].rows).toHaveLength(1)
  })

  it('shows what needs a decision first, then the biggest money', () => {
    const groups = groupRows([
      row({ line: 1, amount: -500, merchantKey: 'LIDL', merchant: 'LIDL' }),
      row({ line: 2, amount: -20, merchantKey: 'KWIACIARNIA', merchant: 'KWIACIARNIA', suggestedCategoryId: null }),
      row({ line: 3, amount: -100, merchantKey: 'ORLEN', merchant: 'ORLEN', suggestedCategoryId: 2 }),
    ])

    expect(groups.map((g) => g.key)).toEqual(['KWIACIARNIA', 'LIDL', 'ORLEN'])
  })

  it('keeps nameless rows apart instead of piling them together', () => {
    // Two unrelated transfers with no merchant in them are not one shop.
    const groups = groupRows([
      row({ line: 1, merchantKey: '', merchant: '', description: 'Przelew 111' }),
      row({ line: 2, merchantKey: '', merchant: '', description: 'Przelew 222' }),
    ])

    expect(groups).toHaveLength(2)
  })
})

describe('undecidedCount', () => {
  it('counts only the groups that are actually going to be imported', () => {
    const groups = groupRows([
      row({ line: 1, suggestedCategoryId: null }),
      row({ line: 2, merchantKey: 'LIDL', suggestedCategoryId: null }),
    ])
    groups[1].include = false

    expect(undecidedCount(groups)).toBe(1)
  })
})

describe('rowsToCommit', () => {
  it('gives every row of a group the category chosen for the group', () => {
    const groups = groupRows([row({ line: 1 }), row({ line: 2 })])
    groups[0].categoryId = 4

    const rows = rowsToCommit(groups)

    expect(rows).toHaveLength(2)
    expect(rows.every((r) => r.categoryId === 4)).toBe(true)
  })

  it('drops switched-off groups', () => {
    const groups = groupRows([row({ line: 1 }), row({ line: 2, merchantKey: 'LIDL' })])
    groups[0].include = false

    expect(rowsToCommit(groups)).toHaveLength(1)
  })

  /// The server would refuse such a row anyway, and half an import that silently never arrived
  /// looks like success.
  it('drops groups nobody chose a category for', () => {
    const groups = groupRows([row({ line: 1, suggestedCategoryId: null })])

    expect(rowsToCommit(groups)).toHaveLength(0)
  })

  it('can also take the duplicates back in, when asked', () => {
    const dup = row({ line: 9, duplicateOfId: 77 })

    expect(rowsToCommit([], [dup], 6)).toHaveLength(1)
    expect(rowsToCommit([], [dup], null)).toHaveLength(0)
  })
})

/// A statement row that is a subscription's charge is not a shop to file: the app is already
/// holding that money. Importing it as an ordinary expense makes the period pay the same bill
/// twice — and the duplicate check cannot catch it when the price has changed, which is
/// exactly when it matters. Claude went 99,16 → 502,67 in one month.
describe('subscription rows', () => {
  const match = (over: Partial<RecurringMatch> = {}): RecurringMatch => ({
    recurringId: 7, name: 'Claude', ruleAmount: 99.16, ruleCurrency: 'PLN',
    learned: true, canUpdateAmount: true,
    chargeId: 55, chargeOn: '2026-09-14', chargeAmount: 99.16, chargeStatus: 'Pending',
    ...over,
  })

  const claude = (over: Partial<ImportRow> = {}, m: Partial<RecurringMatch> = {}) =>
    row({ line: 9, amount: -502.67, merchant: 'ANTHROPIC', merchantKey: 'ANTHROPIC',
          recurring: match(m), ...over })

  it('keeps them out of the shop groups', () => {
    const groups = groupRows([row({ line: 1 }), claude()])

    expect(groups).toHaveLength(1)
    expect(groups[0].merchant).toBe('ZABKA')
  })

  /// A learned match whose price moved is answered "reprice" without being asked: the shop is
  /// known for a fact, and the new figure is right there on the statement.
  it('offers to reprice a known subscription that got dearer', () => {
    const [d] = subscriptionDecisions([claude()])

    expect(d.choice).toBe('reprice')
    expect(subscriptionRowsToCommit([d], null)).toEqual([expect.objectContaining({
      recurringId: 7, updateRecurringAmount: true,
    })])
  })

  it('just confirms one whose price has not moved', () => {
    const [d] = subscriptionDecisions([claude({ amount: -99.16 })])

    expect(d.choice).toBe('confirm')
    expect(subscriptionRowsToCommit([d], null)[0].updateRecurringAmount).toBe(false)
  })

  /// A guess is never acted on silently. Matched on the date alone, it could be the wrong
  /// subscription — and that would confirm a bill that never arrived while the real one stays
  /// unpaid.
  it('leaves a guess unanswered', () => {
    const [d] = subscriptionDecisions([claude({}, { learned: false })])

    expect(d.choice).toBeNull()
    expect(subscriptionRowsToCommit([d], null)).toEqual([])
  })

  /// A rule in euro seen as złoty must not have its price set from the converted figure —
  /// 6,63 EUR would become 29,60 EUR. It is confirmed, not repriced.
  it('does not reprice across currencies', () => {
    const [d] = subscriptionDecisions([
      claude({ amount: -29.6 }, { name: 'Spotify', ruleAmount: 6.63, ruleCurrency: 'EUR', canUpdateAmount: false }),
    ])

    expect(d.choice).toBe('confirm')
  })

  /// Answered "окрема витрата", the row goes back among the shops rather than vanishing.
  it('hands a rejected row back to the ordinary groups', () => {
    const decisions = subscriptionDecisions([claude()]).map((d) => ({ ...d, choice: 'expense' as const }))

    expect(subscriptionRowsToCommit(decisions, null)).toEqual([])
    expect(groupRows(rowsAsPlainExpenses(decisions))).toHaveLength(1)
  })
})
