import { describe, expect, it } from 'vitest'
import { groupRows, rowsToCommit, unansweredGuesses, undecidedCount } from './importGroups'
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
    recurringId: 7, name: 'Spotify', ruleAmount: 6.63, ruleCurrency: 'EUR',
    learned: true, canUpdateAmount: false,
    chargeId: 55, chargeOn: '2026-09-17', chargeAmount: 28.93, chargeStatus: 'Pending',
    ...over,
  })

  const sub = (over: Partial<ImportRow> = {}, m: Partial<RecurringMatch> | null = {}) =>
    row({ line: 9, amount: -29.6, merchant: 'SPOTIFY', merchantKey: 'SPOTIFY',
          recurring: m === null ? null : match(m), ...over })

  /// A learned match is a fact, so it is acted on without being asked.
  it('links a shop the server recognised for certain', () => {
    const [g] = groupRows([sub()])

    expect(g.recurringId).toBe(7)
    expect(rowsToCommit([g])[0]).toMatchObject({ recurringId: 7, updateRecurringAmount: false })
  })

  /// A guess is not. The server matched on the numbers alone, and linking the wrong
  /// subscription would confirm a bill that never arrived and leave the real one unpaid.
  it('leaves a guess for the user to answer', () => {
    const [g] = groupRows([sub({}, { learned: false })])

    expect(g.recurringId).toBeNull()
    expect(unansweredGuesses([g])).toBe(1)
  })

  /// A linked shop needs no category — nothing is created for it — so the usual "no category,
  /// no import" rule must not drop exactly the rows that matter.
  it('imports a linked shop that has no category', () => {
    const [g] = groupRows([sub({ suggestedCategoryId: null })])

    expect(g.categoryId).toBeNull()
    expect(rowsToCommit([g])).toHaveLength(1)
    expect(undecidedCount([g])).toBe(0)
  })

  /// Linked by hand, for the case the app cannot guess: a price that moved too far.
  it('can be linked to a subscription the server did not match', () => {
    const [g] = groupRows([row({ merchantKey: 'ANTHROPIC', merchant: 'ANTHROPIC', amount: -502.67 })])
    expect(g.recurringId).toBeNull()

    const linked = { ...g, recurringId: 7, updateAmount: true }
    expect(rowsToCommit([linked])[0]).toMatchObject({
      recurringId: 7, updateRecurringAmount: true,
    })
  })

  /// Unlinking leaves it an ordinary expense, and it must not carry the flags onwards.
  it('sends nothing about subscriptions for an ordinary shop', () => {
    const [g] = groupRows([row()])

    expect(rowsToCommit([g])[0]).not.toHaveProperty('recurringId')
  })
})
