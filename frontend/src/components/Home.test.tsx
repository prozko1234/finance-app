import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Home } from './Home'
import type { SafeToSpend } from '../types'

function summary(over: Partial<SafeToSpend> = {}): SafeToSpend {
  return {
    date: '2026-07-24', currency: 'PLN', budgetSet: true, monthlyBudget: 3000,
    spentThisMonth: 0, reservedRecurring: 0, remainingThisMonth: 3000, daysLeftInMonth: 8, safeToSpendToday: 375,
    monthTaxes: null,
    ...over,
  }
}

const props = {
  transactions: [], onDelete: vi.fn(), onGoSettings: vi.fn(), onQuickRepeat: vi.fn(), onEdit: vi.fn(),
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

  it('explains the gap between the account and the budget', () => {
    render(
      <Home
        {...props}
        summary={summary({
          monthlyBudget: 15245, spentThisMonth: 1200, remainingThisMonth: 14045,
          monthTaxes: {
            gross: 24600, revenue: 20000, vat: 4600, zusSocial: 1788.19,
            health: 830.58, tax: 2136.23, setAside: 9355, takeHome: 15245,
          },
        })}
      />,
    )

    expect(screen.getByText('Прийшло на рахунок')).toBeInTheDocument()
    expect(screen.getByText(/24 600,00/)).toBeInTheDocument()
    expect(screen.getByText('Відкладено на податки')).toBeInTheDocument()
    expect(screen.getByText(/− 9355,00/)).toBeInTheDocument()
  })

  it('hides the month summary when there is no income to explain', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.queryByText('Підсумок місяця')).not.toBeInTheDocument()
  })
})
