import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Home } from './Home'
import { shiftIso, todayIso } from '../types'
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
    carryover: null,
    reservedDebts: 0,
    pendingCharges: [],
    ...over,
  }
}

const props = {
  transactions: [], canLoadMore: false, onLoadMore: vi.fn(), paydayNudge: null, frequent: [],
  onDelete: vi.fn(), onAddIncome: vi.fn(), onQuickCategory: vi.fn(), onEdit: vi.fn(),
  onGoSavings: vi.fn(), onGoAllocation: vi.fn(), onGoBalance: vi.fn(),
  onDecideCarryover: vi.fn(), onConfirmCharge: vi.fn(),
}

describe('Home', () => {
  it('shows the safe-to-spend number when a budget is set', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.getByText('Можна витратити сьогодні')).toBeInTheDocument()
    expect(screen.getByText(/^375,00/)).toBeInTheDocument() // the headline figure itself
  })

  /// The same question on the first run and at the start of every period: how much arrived.
  /// This used to ask the user to invent a "місячний бюджет".
  it('asks for the income when the period has none yet', () => {
    render(
      <Home
        {...props}
        summary={summary({ budgetSet: false, periodBudget: null, remainingThisPeriod: null, dailyNorm: null, leftToday: null })}
      />,
    )
    expect(screen.getByText(/Вписати дохід/)).toBeInTheDocument()
    expect(screen.getByText(/Період почався 1 липня/)).toBeInTheDocument()
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

    // One line: how much arrived and how much of it goes to taxes. The VAT/ZUS split moved to
    // the tax screen — on the home screen it was a disclosure nobody opens twice.
    expect(screen.getByText(/Прийшло/)).toBeInTheDocument()
    expect(screen.getByText(/24 600,00/)).toBeInTheDocument()
    expect(screen.getByText(/на податки/)).toBeInTheDocument()
    expect(screen.getByText(/9355,00/)).toBeInTheDocument()
    expect(screen.queryByText(/ZUS/)).not.toBeInTheDocument()
  })

  it('shows every pot with what has piled up in it', () => {
    render(
      <Home
        {...props}
        summary={summary({
          envelopes: [
            { id: 1, name: 'Заощадження', kind: 'Savings', isDefault: true, balance: 5000, monthGoal: 2000, depositedThisMonth: 500, stillToReserve: 1500, isFromScheme: false, target: null },
            { id: 2, name: 'Пенсія', kind: 'Investing', isDefault: false, balance: 1200, monthGoal: 600, depositedThisMonth: 0, stillToReserve: 600, isFromScheme: false, target: null },
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
    // A pension bucket used to only subtract from the norm, with nowhere showing it growing.
    expect(screen.getByText(/Пенсія/)).toBeInTheDocument()
    expect(screen.getByText(/1200,00/)).toBeInTheDocument()
    expect(screen.getByText('Відкладено')).toBeInTheDocument()
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

  /// The heading names the window, because "часті" with no period behind it is a claim the
  /// user cannot check — and this row used to be ranked over whatever page of transactions
  /// happened to be loaded, which is exactly the bug the naming makes visible.
  it('offers frequent categories over a named window, without guessing an amount', () => {
    render(
      <Home
        {...props}
        summary={summary()}
        frequent={[
          { categoryId: 1, name: 'Їжа', icon: '🍎', uses: 6, days: 14 },
          { categoryId: 2, name: 'Транспорт', icon: '🚌', uses: 2, days: 14 },
        ]}
      />,
    )

    expect(screen.getByText('Часто за 14 днів')).toBeInTheDocument()
    // The category is offered; no past amount is proposed as a button.
    expect(screen.getByRole('button', { name: /Їжа/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /,00/ })).not.toBeInTheDocument()
  })

  it('hides the period line until there is income to explain', () => {
    render(
      <Home
        {...props}
        summary={summary({ budgetSet: false, periodBudget: null, remainingThisPeriod: null })}
      />,
    )
    expect(screen.queryByText(/лишилось/)).not.toBeInTheDocument()
  })

  /// Three figures on one line. Until M25 this was a column of seven rows and two disclosures:
  /// seeing "скільки лишилось" meant reading the whole month.
  it('puts the period arithmetic in one line and links the схема', async () => {
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
            { id: 1, name: 'Заощадження', kind: 'Savings', isDefault: true, balance: 0, monthGoal: 1200, depositedThisMonth: 0, stillToReserve: 1200, isFromScheme: false, target: null },
            { id: 2, name: 'Борг', kind: 'Debt', isDefault: false, balance: 0, monthGoal: 600, depositedThisMonth: 0, stillToReserve: 600, isFromScheme: false, target: null },
          ],
        })}
      />,
    )

    expect(screen.getByText('Місяць')).toBeInTheDocument()
    expect(screen.getByText(/Бюджет/)).toBeInTheDocument()
    expect(screen.getByText(/лишилось/)).toBeInTheDocument()
    // One line: 1200 into savings + 600 into debt. Two lines would read as holding the same
    // money back twice.
    expect(screen.getByText(/у банках 1800,00/)).toBeInTheDocument()
    // The scheme's buckets are all on the allocation screen, so only the way there stays here.
    expect(screen.getByRole('button', { name: '70/20/10 →' })).toBeInTheDocument()
  })

  /// For someone paid on the 10th, a card headed "Місяць" covering 10.07–09.08 reads as a bug.
  /// So it shows the dates rather than the word.
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

    // Otherwise "витрачено 400" for a month looks as though the app lost something. This used
    // to be a paragraph under the headline figure; now it is the period row's heading — the
    // same thing said where the numbers it refers to actually are.
    expect(screen.getByRole('button', { name: /З 20 липня/ })).toBeInTheDocument()
    expect(screen.getByText(/Було/)).toBeInTheDocument()
  })

  it('keeps the ordinary month wording when the count started on the 1st', () => {
    render(<Home {...props} summary={summary({ spentThisPeriod: 400 })} />)

    expect(screen.queryByRole('button', { name: /^З / })).not.toBeInTheDocument()
    expect(screen.getByText(/Бюджет/)).toBeInTheDocument()
  })

  it('offers to split the budget while the default scheme is on', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.getByRole('button', { name: 'Розподіл →' })).toBeInTheDocument()
  })
  /// Income is editable too: tapping an income row used to do nothing, so correcting an
  /// invoice meant deleting and retyping it — which is exactly where a figure gets lost.
  it('opens an income row for editing, not only expenses', async () => {
    const onEdit = vi.fn()
    const income: Transaction = {
      id: 7, kind: 'Income', amountOriginal: 12300, currencyOriginal: 'PLN', amountBase: 10000,
      grossWithVat: 12300, vatAmount: 2300, amountDisplay: 10000, displayCurrency: 'PLN',
      fxRate: 1, fxDate: '2026-07-24', categoryId: 1, categoryName: 'Дохід',
      frequency: 'OneOff', source: 'Manual', amountIncludesVat: true, date: '2026-07-24', createdAt: '',
    }

    render(<Home {...props} summary={summary()} transactions={[income]} onEdit={onEdit} />)
    await userEvent.setup().click(screen.getByText('Дохід'))

    expect(onEdit).toHaveBeenCalledWith(income)
  })
})

describe('Home — the recent list', () => {
  function tx(over: Partial<Transaction> = {}): Transaction {
    return {
      id: 1, kind: 'Expense', amountOriginal: 25, currencyOriginal: 'PLN', amountBase: 25,
      amountDisplay: 25, displayCurrency: 'PLN', fxRate: 1, fxDate: '2026-07-30',
      categoryId: 1, categoryName: 'Їжа', frequency: 'OneOff', source: 'Manual',
      amountIncludesVat: false, date: '2026-07-30', note: null, createdAt: '2026-07-30T10:00:00Z',
      ...over,
    }
  }

  /// The date used to stand on every row separately and added nothing.
  it('groups the rows by day instead of repeating the date on each', () => {
    render(<Home {...props} summary={summary()} transactions={[
      tx({ id: 1, date: todayIso() }),
      tx({ id: 2, date: shiftIso(todayIso(), -1), categoryName: 'Транспорт' }),
    ]} />)

    expect(screen.getByText('Сьогодні')).toBeInTheDocument()
    expect(screen.getByText('Вчора')).toBeInTheDocument()
  })

  it('offers more rows only while there may be more', () => {
    const onLoadMore = vi.fn()
    const { rerender } = render(
      <Home {...props} summary={summary()} transactions={[tx()]} canLoadMore={false} />)
    expect(screen.queryByText('Показати ще')).not.toBeInTheDocument()

    rerender(
      <Home {...props} summary={summary()} transactions={[tx()]} canLoadMore onLoadMore={onLoadMore} />)
    screen.getByText('Показати ще').click()
    expect(onLoadMore).toHaveBeenCalled()
  })
})

describe('Home — the payday question', () => {
  /// Onboarding only shows on an empty app, so anyone who already had data never saw this
  /// question — and lived with a period starting on the 1st.
  it('asks once and goes away for good', async () => {
    const onGo = vi.fn()
    const onDismiss = vi.fn()
    const user = userEvent.setup()
    render(<Home {...props} summary={summary()} paydayNudge={{ onGo, onDismiss }} />)

    expect(screen.getByText('Коли до тебе приходять гроші?')).toBeInTheDocument()
    await user.click(screen.getByText('Гроші приходять 1-го'))
    expect(onDismiss).toHaveBeenCalled()
  })

  it('says nothing when there is nothing to ask about', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.queryByText('Коли до тебе приходять гроші?')).not.toBeInTheDocument()
  })

  /// The leftover used to vanish at a period boundary: a new period's budget is the new
  /// income, so what went unspent existed only in the bank account.
  it('asks where last period\'s leftover should go, and defaults to the jar', async () => {
    const onDecideCarryover = vi.fn()
    render(
      <Home
        {...props}
        onDecideCarryover={onDecideCarryover}
        summary={summary({
          carryover: {
            amount: 340, fromStart: '2026-06-01', fromEnd: '2026-06-30', envelopeName: 'Подушка',
          },
        })}
      />,
    )

    expect(screen.getByText(/Минулого періоду лишилось 340,00/)).toBeInTheDocument()

    await userEvent.click(screen.getByText(/У банку «Подушка»/))
    expect(onDecideCarryover).toHaveBeenCalledWith('ToEnvelope')
  })

  it('says nothing about a leftover when there is none to place', () => {
    render(<Home {...props} summary={summary()} />)
    expect(screen.queryByText(/Минулого періоду лишилось/)).not.toBeInTheDocument()
  })

  /// Money held back for a debt is missing from the daily norm, and the reason has to be on
  /// the same screen as the figure — named separately from the jars, because a debt is not
  /// money the user still has.
  it('names what debts are holding back', () => {
    render(<Home {...props} summary={summary({ reservedDebts: 250 })} />)
    expect(screen.getByText(/на борги 250,00/)).toBeInTheDocument()
  })

  /// A due date is a schedule, not a receipt. The charge is asked about rather than assumed,
  /// and both answers are one tap — "не пішло" goes through the ordinary delete, which is what
  /// records the skip and brings the undo bar with it.
  describe('a charge waiting to be confirmed', () => {
    const charge = {
      transactionId: 7, name: 'Netflix', amountOriginal: 15.99,
      currencyOriginal: 'USD', amountDisplay: 63.84, date: '2026-07-20',
    }

    it('asks about it, in the currency it was entered in and in the one being read', () => {
      render(<Home {...props} summary={summary({ pendingCharges: [charge] })} />)

      expect(screen.getByText('Netflix')).toBeInTheDocument()
      // Both figures on one line: the dollars are what the bank's page says, the złoty are
      // what every other number on this screen is in.
      expect(screen.getByText(/15,99.*≈.*63,84/)).toBeInTheDocument()
    })

    it('confirms with the charge id, not the subscription id', async () => {
      const onConfirmCharge = vi.fn()
      render(
        <Home {...props} summary={summary({ pendingCharges: [charge] })} onConfirmCharge={onConfirmCharge} />,
      )

      await userEvent.click(screen.getByText(/Оплачено/))
      expect(onConfirmCharge).toHaveBeenCalledWith(7)
    })

    it('sends "не пішло" through the ordinary delete, so undo works', async () => {
      const onDelete = vi.fn()
      render(<Home {...props} summary={summary({ pendingCharges: [charge] })} onDelete={onDelete} />)

      await userEvent.click(screen.getByText('Не пішло'))
      expect(onDelete).toHaveBeenCalledWith(7)
    })

    it('stays out of the way when there is nothing to confirm', () => {
      render(<Home {...props} summary={summary()} />)
      expect(screen.queryByText(/Мало списатись/)).not.toBeInTheDocument()
    })
  })
})
