import { describe, expect, it } from 'vitest'
import { groupRows, rowsToCommit, undecidedCount } from './importGroups'
import type { ImportRow } from './types'

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

  /// Сервер відмовив би такому рядку все одно, а половина імпорту, що мовчки не доїхала,
  /// виглядає як успіх.
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
