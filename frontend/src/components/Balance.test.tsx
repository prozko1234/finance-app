import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Balance } from './Balance'
import type { MonthlyNeed, OpeningBalance, SafeToSpend } from '../types'

function data(over: Partial<OpeningBalance> = {}): OpeningBalance {
  return { isSet: true, amount: 1800, currency: 'PLN', date: '2026-07-25', appliesNow: true, ...over }
}

/// Spendable 2 000 + one jar of 5 000 + 300 held back = 7 300 the app believes is in the
/// account. Every reconcile assertion below is against that figure.
function summary(over: Partial<SafeToSpend> = {}): SafeToSpend {
  return {
    date: '2026-07-24', currency: 'PLN', budgetSet: true, periodBudget: 3000,
    spentThisPeriod: 0, reservedRecurring: 300, remainingThisPeriod: 2000, daysLeftInPeriod: 8,
    dailyNorm: 250, spentToday: 0, leftToday: 250, tomorrowIfStop: 250, tomorrowIfOnPlan: 250,
    monthTaxes: null,
    envelopes: [{
      id: 1, name: 'Подушка', kind: 'Savings', isDefault: true, balance: 5000,
      monthGoal: 0, depositedThisMonth: 0, stillToReserve: 0, isFromScheme: false, target: null,
    }],
    allocation: null,
    windowStart: '2026-07-01', fromOpeningBalance: false,
    periodStart: '2026-07-01', periodEnd: '2026-07-31',
    carryover: null,
    reservedDebts: 0,
    pendingCharges: [],
    daysThisWeek: 7, leftThisWeek: 1750,
    ...over,
  }
}

function need(over: Partial<MonthlyNeed> = {}): MonthlyNeed {
  return {
    currency: 'PLN', recurring: 260, jars: 500, debts: 0,
    typical: 1750, total: 2510, typicalKnown: true,
    ...over,
  }
}

const props = {
  currency: 'PLN',
  onBack: vi.fn(),
  summary: summary(),
  need: need(),
  onRecordGap: vi.fn<(kind: 'expense' | 'income', amount: number) => Promise<void>>()
    .mockResolvedValue(undefined),
}

describe('Мої гроші', () => {
  /// "Скільки в мене взагалі" used to be a question you answered by opening two screens and
  /// adding — the daily norm on one, the jars on another.
  it('adds up the piles the money is actually in', () => {
    render(<Balance {...props} data={data()} onSet={vi.fn()} onClear={vi.fn()} />)

    expect(screen.getByText('Скільки в мене')).toBeInTheDocument()
    expect(screen.getByText(/7300,00/)).toBeInTheDocument() // 2000 + 5000 + 300
    expect(screen.getByText(/2000,00/)).toBeInTheDocument()
    expect(screen.getByText(/5000,00/)).toBeInTheDocument()
  })

  /// The other half: a balance that looks healthy against a month that costs more is the
  /// number people get wrong.
  it('says what the month will ask for, line by line', () => {
    render(<Balance {...props} data={data()} onSet={vi.fn()} onClear={vi.fn()} />)

    expect(screen.getByText('Треба на місяць')).toBeInTheDocument()
    expect(screen.getByText(/2510,00/)).toBeInTheDocument()
    expect(screen.getByText(/260,00/)).toBeInTheDocument()
    expect(screen.getByText(/медіана 3 міс/)).toBeInTheDocument()
  })

  /// A figure invented from a fortnight is worse than no figure, and the card has to say which
  /// of the two it is showing.
  it('admits when usual spending is not known yet', () => {
    render(
      <Balance
        {...props}
        need={need({ typical: null, typicalKnown: false, total: 760 })}
        data={data()}
        onSet={vi.fn()}
        onClear={vi.fn()}
      />,
    )

    expect(screen.getByText(/треба два повні місяці/)).toBeInTheDocument()
  })

  describe('звірка з банком', () => {
    it('names the gap and offers to write it down as a missed expense', async () => {
      const onRecordGap = vi.fn().mockResolvedValue(undefined)
      render(
        <Balance {...props} data={data()} onSet={vi.fn()} onClear={vi.fn()} onRecordGap={onRecordGap} />,
      )

      await userEvent.type(screen.getByLabelText('Скільки насправді на рахунку'), '7157')

      expect(screen.getByText(/у банку менше, ніж я думав/)).toBeInTheDocument()
      await userEvent.click(screen.getByText('Була витрата, яку не записав'))

      await waitFor(() => expect(onRecordGap).toHaveBeenCalledWith('expense', 143))
    })

    it('offers the other direction when the bank has more', async () => {
      const onRecordGap = vi.fn().mockResolvedValue(undefined)
      render(
        <Balance {...props} data={data()} onSet={vi.fn()} onClear={vi.fn()} onRecordGap={onRecordGap} />,
      )

      await userEvent.type(screen.getByLabelText('Скільки насправді на рахунку'), '7500')
      await userEvent.click(screen.getByText('Був дохід, який не записав'))

      await waitFor(() => expect(onRecordGap).toHaveBeenCalledWith('income', 200))
    })

    it('says so when nothing has drifted', async () => {
      render(<Balance {...props} data={data()} onSet={vi.fn()} onClear={vi.fn()} />)

      await userEvent.type(screen.getByLabelText('Скільки насправді на рахунку'), '7300')

      expect(screen.getByText(/Збігається/)).toBeInTheDocument()
      expect(screen.queryByText(/Була витрата/)).not.toBeInTheDocument()
    })

    /// Subtracting a hryvnia count from a złoty total would produce a difference of several
    /// thousand and mean nothing — so it is not shown at all.
    it('refuses to compare across currencies, but still lets the figure be set', async () => {
      const onSet = vi.fn().mockResolvedValue(undefined)
      render(<Balance {...props} data={data()} onSet={onSet} onClear={vi.fn()} />)

      await userEvent.type(screen.getByLabelText('Скільки насправді на рахунку'), '7157')
      await userEvent.selectOptions(screen.getByLabelText('Валюта'), 'UAH')

      expect(screen.queryByText(/Різниця/)).not.toBeInTheDocument()
      expect(screen.getByText(/різницю в UAH я не покажу/)).toBeInTheDocument()

      await userEvent.click(screen.getByText(/Просто вирівняй/))
      await waitFor(() => expect(onSet).toHaveBeenCalledTimes(1))
      expect(onSet.mock.calls[0][0].currency).toBe('UAH')
    })

    it('sends the figure with the day it was looked at', async () => {
      const onSet = vi.fn().mockResolvedValue(undefined)
      render(<Balance {...props} data={data({ isSet: false, amount: null, date: null })} onSet={onSet} onClear={vi.fn()} />)

      await userEvent.type(screen.getByLabelText('Скільки насправді на рахунку'), '2400')
      await userEvent.click(screen.getByText(/Просто вирівняй/))

      await waitFor(() => expect(onSet).toHaveBeenCalledTimes(1))
      const [payload] = onSet.mock.calls[0]
      expect(payload.amount).toBe(2400)
      expect(payload.currency).toBe('PLN')
      expect(payload.date).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    })
  })

  describe('вирівнювання, що діє', () => {
    it('says what is in force and lets it be taken back out', async () => {
      const onClear = vi.fn().mockResolvedValue(undefined)
      render(<Balance {...props} data={data()} onSet={vi.fn()} onClear={onClear} />)

      expect(screen.getByText('Діє зараз')).toBeInTheDocument()
      expect(screen.getByText(/1800,00/)).toBeInTheDocument()

      await userEvent.click(screen.getByText(/Прибрати/))
      await waitFor(() => expect(onClear).toHaveBeenCalledTimes(1))
    })

    /// A count from a period that has ended changes nothing today. It stays, because "чому
    /// норма така" is sometimes answered by "бо ти вирівнював 3-го", but at the weight of a
    /// footnote.
    it('demotes one that no longer applies to a single line', () => {
      render(<Balance {...props} data={data({ appliesNow: false })} onSet={vi.fn()} onClear={vi.fn()} />)

      expect(screen.getByText(/Востаннє вирівнював/)).toBeInTheDocument()
      expect(screen.queryByText('Діє зараз')).not.toBeInTheDocument()
      expect(screen.queryByText(/Прибрати/)).not.toBeInTheDocument()
    })

    it('says nothing at all while nothing has been aligned', () => {
      render(<Balance {...props} data={data({ isSet: false, amount: null, date: null })} onSet={vi.fn()} onClear={vi.fn()} />)

      expect(screen.queryByText('Діє зараз')).not.toBeInTheDocument()
      expect(screen.queryByText(/Востаннє вирівнював/)).not.toBeInTheDocument()
    })
  })
})
