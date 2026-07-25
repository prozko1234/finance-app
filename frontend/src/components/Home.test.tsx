import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Home } from './Home'
import type { SafeToSpend } from '../types'

function summary(over: Partial<SafeToSpend> = {}): SafeToSpend {
  return {
    date: '2026-07-24', currency: 'PLN', budgetSet: true, monthlyBudget: 3000,
    spentThisMonth: 0, reservedRecurring: 0, remainingThisMonth: 3000, daysLeftInMonth: 8, safeToSpendToday: 375,
    ...over,
  }
}

describe('Home', () => {
  it('shows the safe-to-spend number when a budget is set', () => {
    render(<Home summary={summary()} transactions={[]} onDelete={vi.fn()} onGoSettings={vi.fn()} onQuickRepeat={vi.fn()} onEdit={vi.fn()} />)
    expect(screen.getByText(/375,00/)).toBeInTheDocument()
  })

  it('prompts to set a budget when none is set', () => {
    render(
      <Home
        summary={summary({ budgetSet: false, monthlyBudget: null, remainingThisMonth: null, safeToSpendToday: null })}
        transactions={[]}
        onDelete={vi.fn()}
        onGoSettings={vi.fn()}
        onQuickRepeat={vi.fn()}
        onEdit={vi.fn()}
      />,
    )
    expect(screen.getByText(/Задати місячний бюджет/)).toBeInTheDocument()
  })
})
