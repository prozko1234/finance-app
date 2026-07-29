import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { Home } from './Home'
import type { SafeToSpend, Transaction } from '../types'

function summary(over: Partial<SafeToSpend> = {}): SafeToSpend {
  return {
    date: '2026-07-24', currency: 'PLN', budgetSet: true, periodBudget: 3000,
    spentThisPeriod: 0, reservedRecurring: 0, remainingThisPeriod: 3000, daysLeftInPeriod: 8,
    dailyNorm: 375, spentToday: 0, leftToday: 375, tomorrowIfStop: 375, tomorrowIfOnPlan: 375,
    monthTaxes: null,
    envelopes: [],
    allocation: null,
    windowStart: '2026-07-01', fromOpeningBalance: false,
    periodStart: '2026-07-01', periodEnd: '2026-07-31',
    ...over,
  }
}

const props = {
  transactions: [], onDelete: vi.fn(), onGoSettings: vi.fn(), onQuickCategory: vi.fn(), onEdit: vi.fn(),
  onGoSavings: vi.fn(), onGoAllocation: vi.fn(),
}

describe('Home', () => {
  it('shows the safe-to-spend number when a budget is set', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.getByText('Можна витратити сьогодні')).toBeInTheDocument()
    expect(screen.getByText(/^375,00/)).toBeInTheDocument() // the headline figure itself
  })

  it('prompts to set a budget when none is set', () => {
    render(
      <Home
        {...props}
        summary={summary({ budgetSet: false, periodBudget: null, remainingThisPeriod: null, dailyNorm: null, leftToday: null })}
      />,
    )
    expect(screen.getByText(/Задати місячний бюджет/)).toBeInTheDocument()
  })

  it('explains the gap between the account and the budget', () => {
    render(
      <Home
        {...props}
        summary={summary({
          periodBudget: 15245, spentThisPeriod: 1200, remainingThisPeriod: 14045,
          monthTaxes: {
            gross: 24600, revenue: 20000, vat: 4600, zusSocial: 1788.19,
            health: 830.58, tax: 2136.23, setAside: 9355, takeHome: 15245,
            currency: 'PLN', ratesYear: 2026,
          },
        })}
      />,
    )

    expect(screen.getByText('Прийшло на рахунок')).toBeInTheDocument()
    expect(screen.getByText(/24 600,00/)).toBeInTheDocument()
    expect(screen.getByText('Відкладено на податки')).toBeInTheDocument()
    expect(screen.getByText(/− 9355,00/)).toBeInTheDocument()
  })

  it('shows every pot with what has piled up in it', () => {
    render(
      <Home
        {...props}
        summary={summary({
          envelopes: [
            { id: 1, name: 'Заощадження', kind: 'Savings', isDefault: true, balance: 5000, monthGoal: 2000, depositedThisMonth: 500, stillToReserve: 1500 },
            { id: 2, name: 'Пенсія', kind: 'Investing', isDefault: false, balance: 1200, monthGoal: 600, depositedThisMonth: 0, stillToReserve: 600 },
          ],
          monthTaxes: {
            gross: 24600, revenue: 20000, vat: 4600, zusSocial: 1788.19,
            health: 830.58, tax: 2136.23, setAside: 9355, takeHome: 15245,
            currency: 'PLN', ratesYear: 2026,
          },
        })}
      />,
    )

    expect(screen.getByText(/Заощадження/)).toBeInTheDocument()
    expect(screen.getByText(/5000,00/)).toBeInTheDocument()
    // Пенсія раніше тільки віднімалась від норми і ніде не було видно, що вона росте.
    expect(screen.getByText(/Пенсія/)).toBeInTheDocument()
    expect(screen.getByText(/1200,00/)).toBeInTheDocument()
    expect(screen.getByText('Відкладено у конверти')).toBeInTheDocument()
  })

  it('offers to set up saving when the envelope is empty', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.getByText(/Відкладати щомісяця/)).toBeInTheDocument()
  })

  it('names today\'s overspending and what it costs tomorrow', () => {
    render(
      <Home
        {...props}
        summary={summary({
          spentThisPeriod: 300, spentToday: 300, dailyNorm: 176.47, leftToday: -123.53,
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
    expect(screen.getByText('Можна витратити сьогодні')).toBeInTheDocument()
  })

  it('offers frequent categories without guessing an amount', () => {
    const expense = (id: number, categoryId: number, categoryName: string, amount: number): Transaction => ({
      id, kind: 'Expense', amountOriginal: amount, currencyOriginal: 'PLN', amountBase: amount,
      amountDisplay: amount, displayCurrency: 'PLN', fxRate: 1, fxDate: '2026-07-24', categoryId, categoryName, priority: 'Should',
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

  it('puts the month arithmetic in one column, схема included', async () => {
    render(
      <Home
        {...props}
        summary={summary({
          spentThisPeriod: 500, periodBudget: 6000, remainingThisPeriod: 4300,
          allocation: {
            schemeName: '70/20/10', preset: '70-20-10', spendable: 4200, reserved: 1800,
            buckets: [
              { id: 1, name: 'Витрати', kind: 'Spending', percent: 70, amount: 4200 },
              { id: 2, name: 'Заощадження', kind: 'Savings', percent: 20, amount: 1200 },
              { id: 3, name: 'Борг', kind: 'Debt', percent: 10, amount: 600 },
            ],
          },
          envelopes: [
            { id: 1, name: 'Заощадження', kind: 'Savings', isDefault: true, balance: 0, monthGoal: 1200, depositedThisMonth: 0, stillToReserve: 1200 },
            { id: 2, name: 'Борг', kind: 'Debt', isDefault: false, balance: 0, monthGoal: 600, depositedThisMonth: 0, stillToReserve: 600 },
          ],
        })}
      />,
    )

    expect(screen.getByText('Місяць')).toBeInTheDocument()
    expect(screen.getByText('Бюджет періоду')).toBeInTheDocument()
    // Одним рядком: 1200 у заощадження + 600 у борг. Два рядки читались би як
    // подвійне утримання тих самих грошей.
    expect(screen.getByText('Відкладено у конверти')).toBeInTheDocument()
    expect(screen.getByText('− 1800,00 zł')).toBeInTheDocument()
    expect(screen.getByText(/куди пішов бюджет/)).toBeInTheDocument()
  })

  /// Для людини, якій платять 10-го, картка «Місяць» з періодом 10.07–09.08 читається
  /// як помилка додатка. Тому там дати, а не слово.
  it('names the period by its dates when payday is not the 1st', () => {
    render(
      <Home
        {...props}
        summary={summary({ periodStart: '2026-07-10', periodEnd: '2026-08-09' })}
      />,
    )

    expect(screen.getByText('10 липня – 9 серпня')).toBeInTheDocument()
    expect(screen.queryByText('Місяць')).not.toBeInTheDocument()
  })

  it('says out loud that the count starts mid-month', () => {
    render(
      <Home
        {...props}
        summary={summary({ fromOpeningBalance: true, windowStart: '2026-07-20', spentThisPeriod: 400 })}
      />,
    )

    // Інакше «витрачено 400» за місяць виглядає так, ніби додаток щось загубив.
    expect(screen.getByText(/Рахуємо з 20 липня/)).toBeInTheDocument()
    expect(screen.getByText('Було на руках')).toBeInTheDocument()
    expect(screen.getByText(/Витрачено з 20 липня/)).toBeInTheDocument()
  })

  it('keeps the ordinary month wording when the count started on the 1st', () => {
    render(<Home {...props} summary={summary({ spentThisPeriod: 400 })} />)

    expect(screen.queryByText(/Рахуємо з/)).not.toBeInTheDocument()
    expect(screen.getByText('Бюджет періоду')).toBeInTheDocument()
  })

  it('offers to split the budget while the default scheme is on', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.getByText(/Ділити бюджет за схемою/)).toBeInTheDocument()
  })
})
