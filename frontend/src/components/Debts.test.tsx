import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Debts } from './Debts'
import type { Debt, Debts as DebtsData, EnvelopeSummary, SaveDebtPayment } from '../types'

function debt(over: Partial<Debt> = {}): Debt {
  return {
    id: 1, direction: 'IOwe', person: 'Сергій',
    amount: 1000, amountOriginal: 1000, currencyOriginal: 'PLN',
    date: '2026-07-01', deadline: null, reserveFromBudget: false,
    paid: 0, outstanding: 1000, perPeriod: 0, periodsLeft: 0,
    overdue: false, closedOn: null, note: null, payments: [],
    ...over,
  }
}

function data(over: Partial<DebtsData> = {}): DebtsData {
  return {
    currency: 'PLN', iOweTotal: 0, theyOweMeTotal: 0, reservedThisPeriod: 0,
    iOwe: [], theyOweMe: [],
    ...over,
  }
}

const jar: EnvelopeSummary = {
  id: 7, name: 'Подушка', kind: 'Savings', isDefault: true,
  balance: 2000, monthGoal: 0, depositedThisMonth: 0, stillToReserve: 0,
  isFromScheme: false, target: null,
}

function renderScreen(d: DebtsData, handlers: Partial<{
  onPay: (id: number, p: SaveDebtPayment) => Promise<void>
  onSetClosed: (id: number, closed: boolean) => Promise<void>
}> = {}) {
  render(
    <Debts
      data={d}
      envelopes={[jar]}
      onCreate={vi.fn()}
      onDelete={vi.fn()}
      onSetClosed={handlers.onSetClosed ?? vi.fn()}
      onPay={handlers.onPay ?? vi.fn()}
      onBack={vi.fn()}
    />,
  )
}

describe('Debts', () => {
  /// The bug this screen exists to fix: as a jar, a debt's number GREW as it was paid off.
  /// What the card leads with has to be what is still owed.
  it('leads with what is still owed, not with what has been paid', () => {
    renderScreen(data({
      iOweTotal: 600,
      iOwe: [debt({ amount: 1000, paid: 400, outstanding: 600 })],
    }))

    // Twice on purpose: the side's total and the debt's own remainder are the same money.
    expect(screen.getAllByText('600,00 zł')).toHaveLength(2)
    // The original sum is context, not the headline; what was paid is not a figure at all.
    // Matched loosely: the formatter separates thousands with a non-breaking space, and a
    // test that hard-codes which kind of space breaks on the next locale tweak.
    expect(screen.getByText(/^з\s.*000,00\szł$/)).toBeInTheDocument()
    expect(screen.queryByText('400,00 zł')).not.toBeInTheDocument()
  })

  /// Money missing from the daily norm needs something on screen explaining it.
  it('says out loud what debts are holding back this period', () => {
    renderScreen(data({ reservedThisPeriod: 250, iOwe: [debt({ perPeriod: 250 })] }))

    expect(screen.getByText(/тримається на борги/)).toBeInTheDocument()
  })

  it('says nothing about a reserve when nothing is held back', () => {
    renderScreen(data({ iOwe: [debt()] }))

    expect(screen.queryByText(/тримається на борги/)).not.toBeInTheDocument()
  })

  /// The source is the feature. Paying from a jar must be offered for money going out.
  it('offers a jar as a source when paying somebody back', async () => {
    const user = userEvent.setup()
    renderScreen(data({ iOwe: [debt()] }))

    await user.click(screen.getByRole('button', { name: 'Погасив' }))

    expect(screen.getByRole('button', { name: 'З банки' })).toBeInTheDocument()
  })

  /// Money coming back is arriving, not leaving: there is no jar to take it out of, and
  /// offering one would be offering a way to make a pot smaller for money going into it.
  it('does not offer a jar for money coming back', async () => {
    const user = userEvent.setup()
    renderScreen(data({ theyOweMe: [debt({ id: 2, direction: 'TheyOweMe', person: 'Оля' })] }))

    await user.click(screen.getByRole('button', { name: 'Повернули' }))

    expect(screen.queryByRole('button', { name: 'З банки' })).not.toBeInTheDocument()
  })

  it('sends the amount, the date and the source it was told', async () => {
    const user = userEvent.setup()
    const onPay = vi.fn().mockResolvedValue(undefined)
    renderScreen(data({ iOwe: [debt()] }), { onPay })

    await user.click(screen.getByRole('button', { name: 'Погасив' }))
    await user.type(screen.getByLabelText('Сума'), '400')
    await user.click(screen.getByRole('button', { name: 'З банки' }))
    await user.click(screen.getByRole('button', { name: 'Записати' }))

    await waitFor(() => expect(onPay).toHaveBeenCalledWith(1, expect.objectContaining({
      amount: 400, source: 'Envelope', envelopeId: 7,
    })))
  })

  /// Each source does something different to the daily norm, and the form says which before
  /// the money is written down rather than after.
  it('explains what each source does to the daily norm', async () => {
    const user = userEvent.setup()
    renderScreen(data({ iOwe: [debt()] }))

    await user.click(screen.getByRole('button', { name: 'Погасив' }))
    expect(screen.getByText(/Денна норма впаде/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'З банки' }))
    expect(screen.getByText(/Норма не впаде/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Віддав раніше' }))
    expect(screen.getByText(/Цей період за них не платить/)).toBeInTheDocument()
  })

  /// Debts get forgiven and rounded off; the list has to be clearable whatever the sums say.
  it('can call a debt finished with money still on it', async () => {
    const user = userEvent.setup()
    const onSetClosed = vi.fn().mockResolvedValue(undefined)
    renderScreen(data({ iOwe: [debt({ outstanding: 600 })] }), { onSetClosed })

    await user.click(screen.getByRole('button', { name: 'Закрити' }))

    expect(onSetClosed).toHaveBeenCalledWith(1, true)
  })

  /// Closed debts are history: readable, but not at the top of a list about what is still owed.
  it('keeps closed debts folded away', () => {
    renderScreen(data({ iOwe: [debt({ closedOn: '2026-08-01' })] }))

    expect(screen.getByText('Закриті: 1')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Погасив' })).not.toBeInTheDocument()
  })

  it('shows an overdue debt as overdue', () => {
    renderScreen(data({ iOwe: [debt({ deadline: '2026-07-01', overdue: true })] }))

    expect(screen.getByText(/Дедлайн минув/)).toBeInTheDocument()
  })
})
