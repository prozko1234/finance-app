import { describe, it, expect } from 'vitest'
import { buildQuickActions } from './quickRepeat'
import type { Transaction } from './types'

function tx(over: Partial<Transaction>): Transaction {
  return {
    id: 1, kind: 'Expense', amountOriginal: 25, currencyOriginal: 'PLN', amountBase: 25,
    fxRate: 1, fxDate: '2026-07-25', categoryId: 1, categoryName: 'Їжа', priority: 'Should',
    frequency: 'OneOff', source: 'Manual', date: '2026-07-25', createdAt: '', ...over,
  }
}

const icon = () => '🍽'

describe('buildQuickActions', () => {
  it('collapses repeated category+amount into one action', () => {
    const actions = buildQuickActions([tx({ id: 1 }), tx({ id: 2 }), tx({ id: 3 })], icon)
    expect(actions).toHaveLength(1)
    expect(actions[0].amount).toBe(25)
  })

  it('keeps distinct amounts separate, most recent first', () => {
    const actions = buildQuickActions(
      [tx({ id: 1, amountOriginal: 25 }), tx({ id: 2, amountOriginal: 12 })],
      icon,
    )
    expect(actions.map((a) => a.amount)).toEqual([25, 12])
  })

  it('ignores income', () => {
    const actions = buildQuickActions([tx({ id: 1, kind: 'Income' }), tx({ id: 2 })], icon)
    expect(actions).toHaveLength(1)
    expect(actions[0].categoryId).toBe(1)
  })

  it('respects the limit', () => {
    const many = [10, 20, 30, 40, 50].map((a, i) => tx({ id: i, amountOriginal: a }))
    expect(buildQuickActions(many, icon)).toHaveLength(3)
  })

  it('returns nothing when there is no history', () => {
    expect(buildQuickActions([], icon)).toEqual([])
  })
})
