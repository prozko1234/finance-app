import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Home } from './Home'
import type { SafeToSpend, Transaction } from '../types'

function summary(over: Partial<SafeToSpend> = {}): SafeToSpend {
  return {
    date: '2026-07-24', currency: 'PLN', budgetSet: true, monthlyBudget: 3000,
    spentThisMonth: 0, reservedRecurring: 0, remainingThisMonth: 3000, daysLeftInMonth: 8,
    dailyNorm: 375, spentToday: 0, leftToday: 375, tomorrowIfStop: 375, tomorrowIfOnPlan: 375,
    monthTaxes: null,
    savings: { balance: 0, monthGoal: 0, depositedThisMonth: 0, stillToReserve: 0 },
    ...over,
  }
}

const props = {
  transactions: [], onDelete: vi.fn(), onGoSettings: vi.fn(), onQuickCategory: vi.fn(), onEdit: vi.fn(),
  onGoSavings: vi.fn(),
}

describe('Home', () => {
  it('shows the safe-to-spend number when a budget is set', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.getByText('Ще сьогодні')).toBeInTheDocument()
    expect(screen.getByText(/^375,00/)).toBeInTheDocument() // the headline figure itself
  })

  it('prompts to set a budget when none is set', () => {
    render(
      <Home
        {...props}
        summary={summary({ budgetSet: false, monthlyBudget: null, remainingThisMonth: null, dailyNorm: null, leftToday: null })}
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

  it('shows the savings envelope as its own number, plus what is still held back', () => {
    render(
      <Home
        {...props}
        summary={summary({
          savings: { balance: 5000, monthGoal: 2000, depositedThisMonth: 500, stillToReserve: 1500 },
          monthTaxes: {
            gross: 24600, revenue: 20000, vat: 4600, zusSocial: 1788.19,
            health: 830.58, tax: 2136.23, setAside: 9355, takeHome: 15245,
          },
        })}
      />,
    )

    expect(screen.getByText('Заощадження')).toBeInTheDocument()
    expect(screen.getByText(/5000,00/)).toBeInTheDocument()
    expect(screen.getByText('Ще у заощадження цього місяця')).toBeInTheDocument()
  })

  it('offers to set up saving when the envelope is empty', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.getByText(/Відкладати у заощадження щомісяця/)).toBeInTheDocument()
  })

  it('names today\'s overspending and what it costs tomorrow', () => {
    render(
      <Home
        {...props}
        summary={summary({
          spentThisMonth: 300, spentToday: 300, dailyNorm: 176.47, leftToday: -123.53,
          tomorrowIfStop: 168.75, tomorrowIfOnPlan: 176.47,
        })}
      />,
    )

    expect(screen.getByText('Понад норму сьогодні')).toBeInTheDocument()
    expect(screen.getByText(/123,53/)).toBeInTheDocument() // shown as a positive overshoot
    expect(screen.getByText(/Завтра 168,75/)).toBeInTheDocument()
  })

  it('stays quiet about tomorrow when nothing was spent today', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.queryByText(/Завтра/)).not.toBeInTheDocument()
    expect(screen.getByText('Ще сьогодні')).toBeInTheDocument()
  })

  it('offers frequent categories without guessing an amount', () => {
    const expense = (id: number, categoryId: number, categoryName: string, amount: number): Transaction => ({
      id, kind: 'Expense', amountOriginal: amount, currencyOriginal: 'PLN', amountBase: amount,
      fxRate: 1, fxDate: '2026-07-24', categoryId, categoryName, priority: 'Should',
      frequency: 'OneOff', source: 'Manual', date: '2026-07-24', createdAt: '',
    })

    render(
      <Home
        {...props}
        summary={summary()}
        transactions={[expense(1, 1, 'Їжа', 25), expense(2, 1, 'Їжа', 12), expense(3, 2, 'Транспорт', 8)]}
      />,
    )

    expect(screen.getByText('Часті категорії')).toBeInTheDocument()
    // The category is offered; the past amounts are not proposed as buttons.
    expect(screen.getAllByText('Їжа').length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: /25,00/ })).not.toBeInTheDocument()
  })

  it('hides the month summary when there is no income to explain', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.queryByText('Підсумок місяця')).not.toBeInTheDocument()
  })
})
