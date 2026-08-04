import { describe, it, expect } from 'vitest'
import { buildQuickCategories } from './quickCategories'
import type { Transaction } from './types'

function tx(over: Partial<Transaction>): Transaction {
  return {
    id: 1, kind: 'Expense', amountOriginal: 25, currencyOriginal: 'PLN', amountBase: 25,
    amountDisplay: 25, displayCurrency: 'PLN', fxRate: 1, fxDate: '2026-07-25', categoryId: 1, categoryName: 'Їжа',
    frequency: 'OneOff', source: 'Manual', amountIncludesVat: false, date: '2026-07-25', createdAt: '', ...over,
  }
}

describe('buildQuickCategories', () => {
  it('collapses a category into one shortcut regardless of amount', () => {
    const actions = buildQuickCategories(
      [tx({ id: 1, amountOriginal: 25 }), tx({ id: 2, amountOriginal: 12 }), tx({ id: 3, amountOriginal: 7 })],
    )

    expect(actions).toHaveLength(1)
    expect(actions[0].uses).toBe(3)
    expect(actions[0].categoryId).toBe(1)
  })

  it('ranks by how often a category is used, not by recency alone', () => {
    const actions = buildQuickCategories(
      [
        tx({ id: 1, categoryId: 2, categoryName: 'Транспорт' }), // newest, but used once
        tx({ id: 2, categoryId: 1 }),
        tx({ id: 3, categoryId: 1 }),
      ],
    )

    expect(actions.map((a) => a.categoryId)).toEqual([1, 2])
  })

  it('breaks ties by most recent use', () => {
    const actions = buildQuickCategories(
      [tx({ id: 1, categoryId: 3, categoryName: 'Житло' }), tx({ id: 2, categoryId: 1 })],
    )

    expect(actions.map((a) => a.categoryId)).toEqual([3, 1])
  })

  it('ignores income', () => {
    const actions = buildQuickCategories([tx({ id: 1, kind: 'Income' }), tx({ id: 2 })])

    expect(actions).toHaveLength(1)
    expect(actions[0].categoryId).toBe(1)
  })

  it('respects the limit', () => {
    const many = [1, 2, 3, 4, 5, 6].map((c) => tx({ id: c, categoryId: c, categoryName: `К${c}` }))
    expect(buildQuickCategories(many)).toHaveLength(4)
  })

  it('carries the emoji the category itself has, not one guessed from its name', () => {
    const actions = buildQuickCategories([tx({ id: 1, categoryName: 'Кава', categoryIcon: '☕' })])

    expect(actions[0].icon).toBe('☕')
  })

  it('returns nothing when there is no history', () => {
    expect(buildQuickCategories([])).toEqual([])
  })
})
