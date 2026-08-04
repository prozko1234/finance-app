import { useState } from 'react'
import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Savings } from './Savings'
import type { EnvelopeSummary, SaveEnvelope, SaveEnvelopeTarget, SaveTransfer, Savings as SavingsData } from '../types'

vi.mock('../hooks', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../hooks')>()),
  useEnvelopeHistory: () => ({
    data: [
      { start: '2026-07-10', end: '2026-08-09', moved: 1200, balanceAfter: 8200 },
      { start: '2026-06-10', end: '2026-07-09', moved: -400, balanceAfter: 7000 },
    ],
  }),
}))

function envelope(over: Partial<EnvelopeSummary> = {}): EnvelopeSummary {
  return {
    id: 1, name: 'Заощадження', kind: 'Savings', isDefault: true,
    balance: 8200, monthGoal: 1200, depositedThisMonth: 1200, stillToReserve: 0,
    isFromScheme: false, target: null,
    ...over,
  }
}

function data(envelopes: EnvelopeSummary[], over: Partial<SavingsData> = {}): SavingsData {
  return {
    mode: 'Percent', value: 20, active: true,
    balance: 8200, monthGoal: 1200, depositedThisMonth: 1200, stillToReserve: 0,
    currency: 'PLN', recent: [], envelopes, goalFromScheme: '70/20/10',
    planPausedFrom: null,
    ...over,
  }
}

function renderScreen(
  d: SavingsData,
  onDeleteEntry: (id: number) => Promise<void> = vi.fn(),
  envelopeHandlers: Partial<{
    onCreateEnvelope: (e: SaveEnvelope) => Promise<void>
    onUpdateEnvelope: (id: number, e: SaveEnvelope) => Promise<void>
    onArchiveEnvelope: (id: number) => Promise<void>
    onSetTarget: (id: number, t: SaveEnvelopeTarget) => Promise<void>
    onTransfer: (t: SaveTransfer) => Promise<void>
  }> = {},
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  // The opened jar lives in the address (see router.ts), so the smallest possible wrapper
  // holds it here — otherwise every test would have to carry that state itself.
  function Harness() {
    const [openId, setOpenId] = useState<number | null>(null)
    return (
      <Savings
        data={d}
        onSavePlan={vi.fn()}
        onAddEntry={vi.fn()}
        onUpdateEntry={vi.fn()}
        onDeleteEntry={onDeleteEntry}
        onCreateEnvelope={vi.fn()}
        onUpdateEnvelope={vi.fn()}
        onArchiveEnvelope={vi.fn()}
        onSetTarget={vi.fn()}
        onTransfer={vi.fn()}
        {...envelopeHandlers}
        openId={openId}
        onOpen={setOpenId}
        onBack={vi.fn()}
      />
    )
  }

  return render(
    <QueryClientProvider client={client}>
      <Harness />
    </QueryClientProvider>,
  )
}

describe('Savings', () => {
  /// The screen used to open on one jar, with no way to see where the rest of the money was.
  it('opens on the list of every envelope with its balance', () => {
    renderScreen(data([envelope(), envelope({ id: 2, name: 'Пенсія', balance: 4200, isDefault: false })]))

    expect(screen.getByText('Відкладено всього')).toBeInTheDocument()
    expect(screen.getByText(/12 400,00/)).toBeInTheDocument() // 8200 + 4200
    expect(screen.getByText('Заощадження')).toBeInTheDocument()
    expect(screen.getByText('Пенсія')).toBeInTheDocument()
  })

  /// The reason the screen was rebuilt: per period, both the movement and the resulting balance.
  it('shows period by period once an envelope is opened', async () => {
    renderScreen(data([envelope()]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Заощадження'))

    expect(screen.getByText('По періодах')).toBeInTheDocument()
    expect(screen.getByText('10 липня – 9 серпня')).toBeInTheDocument()
    expect(screen.getByText('+1200,00 zł')).toBeInTheDocument()
    // Twice on screen: as the headline figure at the top, and as the balance after this period.
    expect(screen.getAllByText('8200,00 zł').length).toBeGreaterThan(1)
    // A withdrawal is shown as honestly as a deposit.
    expect(screen.getByText('−400,00 zł')).toBeInTheDocument()
  })

  /// An active plan with a goal of 0 looks like a broken app unless the reason is on screen.
  it('says why nothing is put aside in a period that started from a count', () => {
    renderScreen(data([envelope({ monthGoal: 0, depositedThisMonth: 0 })], {
      monthGoal: 0, depositedThisMonth: 0, planPausedFrom: '2026-07-20',
    }))

    expect(screen.getByText(/20 липня ти порахував залишок/)).toBeInTheDocument()
  })

  /// A deposit the scheme made could be edited or deleted — and the next screen load brought it
  /// back. An action that appeared to work and then undid itself.
  it('does not let the scheme own deposit be edited or deleted', async () => {
    const onDeleteEntry = vi.fn()
    renderScreen(data([envelope()], {
      recent: [
        {
          id: 10, date: '2026-07-30', kind: 'Deposit', amount: 1200, amountOriginal: 1200,
          currencyOriginal: 'PLN', note: 'За схемою «70/20/10»', envelopeId: 1, envelopeName: 'Заощадження',
          isAuto: true, isTransfer: false,
        },
        {
          id: 11, date: '2026-07-30', kind: 'Deposit', amount: 200, amountOriginal: 200,
          currencyOriginal: 'PLN', note: 'понад план', envelopeId: 1, envelopeName: 'Заощадження',
          isAuto: false, isTransfer: false,
        },
      ],
    }), onDeleteEntry)
    const user = userEvent.setup()

    await user.click(screen.getByText('Заощадження'))

    // A hand-made movement keeps its ✕; a scheme's does not have one.
    expect(screen.getAllByLabelText('Видалити')).toHaveLength(1)
    expect(screen.getByText('за схемою')).toBeInTheDocument()

    await user.click(screen.getAllByLabelText('Видалити')[0])
    await waitFor(() => expect(onDeleteEntry).toHaveBeenCalledWith(11))
  })

  it('does not offer the savings plan on an envelope the scheme drives', async () => {
    renderScreen(data([envelope({ id: 2, name: 'Пенсія', isDefault: false })]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Пенсія'))

    expect(screen.queryByText(/Відкладати щомісяця/)).not.toBeInTheDocument()
  })
  // Jars as a thing in their own right: until now a jar could only be had as a scheme bucket.

  /// The word "банка" invites making one for a holiday — and there was no way to make one.
  it('makes a pot of its own from a name and a kind', async () => {
    const onCreateEnvelope = vi.fn()
    renderScreen(data([envelope()]), vi.fn(), { onCreateEnvelope })
    const user = userEvent.setup()

    await user.click(screen.getByText('+ Нова банка'))
    await user.type(screen.getByPlaceholderText('Відпустка'), 'Ремонт')
    await user.click(screen.getByText('Інше'))
    await user.click(screen.getByText('Створити'))

    await waitFor(() =>
      expect(onCreateEnvelope).toHaveBeenCalledWith({ name: 'Ремонт', kind: 'Other' }))
  })

  it('renames a hand-made pot and puts it away once it is empty', async () => {
    const onUpdateEnvelope = vi.fn()
    const onArchiveEnvelope = vi.fn()
    const own = envelope({ id: 3, name: 'Відпустка', isDefault: false, balance: 0, monthGoal: 0 })
    renderScreen(data([own]), vi.fn(), { onUpdateEnvelope, onArchiveEnvelope })
    const user = userEvent.setup()

    await user.click(screen.getByText('Відпустка'))
    const nameInput = screen.getByDisplayValue('Відпустка')
    await user.clear(nameInput)
    await user.type(nameInput, 'Відпустка 2027')
    await user.click(screen.getByText('Зберегти назву'))

    await waitFor(() => expect(onUpdateEnvelope)
      .toHaveBeenCalledWith(3, { name: 'Відпустка 2027', kind: 'Savings' }))

    await user.click(screen.getByText('Прибрати банку'))
    await waitFor(() => expect(onArchiveEnvelope).toHaveBeenCalledWith(3))
  })

  /// A jar that vanished with money inside would take it out of "Відкладено всього" — the one
  /// figure this app asks to be trusted.
  it('does not offer to put away a pot that still holds money', async () => {
    renderScreen(data([envelope({ id: 3, name: 'Відпустка', isDefault: false, balance: 240 })]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Відпустка'))

    expect(screen.queryByText('Прибрати банку')).not.toBeInTheDocument()
    expect(screen.getByText(/у ній ще 240,00/)).toBeInTheDocument()
  })

  /// A scheme's bucket finds its jar by name, so a rename would quietly hand the balance to a
  /// jar nobody feeds.
  it('does not offer renaming for a pot the scheme owns', async () => {
    renderScreen(data([envelope({ id: 2, name: 'Пенсія', isDefault: false, isFromScheme: true })]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Пенсія'))

    expect(screen.queryByText('Назва й вид')).not.toBeInTheDocument()
    expect(screen.getByText(/задає схема розподілу/)).toBeInTheDocument()
  })
  // A target on a jar: without one, a jar no scheme feeds is a pointless piggy bank.

  it('turns a target with a date into what has to go in each period', async () => {
    renderScreen(data([envelope({
      id: 3, name: 'Відпустка', isDefault: false, balance: 2200, monthGoal: 0,
      target: {
        amount: 6000, date: '2026-10-09', remaining: 3800, periodsLeft: 3,
        perPeriod: 1266.67, reached: false, overdue: false,
      },
    })]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Відпустка'))

    expect(screen.getByText(/до 9 жовтня/)).toBeInTheDocument()
    expect(screen.getByText(/1266,67 zł за період, 3 періоди/)).toBeInTheDocument()
    // The important part, said out loud: a target takes nothing out of the daily norm.
    expect(screen.getByText(/нічого не тримає з «Можна витратити сьогодні»/)).toBeInTheDocument()
  })

  /// The date is optional: "зібрати 6 000" is a goal too, and a deadline must not be invented.
  it('sets a target without a date at all', async () => {
    const onSetTarget = vi.fn()
    renderScreen(data([envelope({ id: 3, name: 'Ремонт', isDefault: false })]), vi.fn(), { onSetTarget })
    const user = userEvent.setup()

    await user.click(screen.getByText('Ремонт'))
    await user.click(screen.getByText('Поставити ціль'))
    await user.type(screen.getByPlaceholderText('6000'), '4000')
    await user.click(screen.getByText('Зберегти'))

    await waitFor(() => expect(onSetTarget)
      .toHaveBeenCalledWith(3, { amount: 4000, currency: 'PLN', date: null }))
  })

  it('takes the target off again', async () => {
    const onSetTarget = vi.fn()
    renderScreen(data([envelope({
      id: 3, name: 'Відпустка', isDefault: false,
      target: {
        amount: 6000, date: null, remaining: 3800, periodsLeft: 0,
        perPeriod: 0, reached: false, overdue: false,
      },
    })]), vi.fn(), { onSetTarget })
    const user = userEvent.setup()

    await user.click(screen.getByText('Відпустка'))
    // With no date there is no pace, and the screen says so rather than showing 0 per period.
    expect(screen.getByText(/Дати немає, тож і темпу немає/)).toBeInTheDocument()

    await user.click(screen.getByText('Прибрати'))
    await waitFor(() => expect(onSetTarget).toHaveBeenCalledWith(3, { amount: null }))
  })
  /// "Внесок у заощадження" under a 🐖 in a jar called "Зобовʼязання" read as a bug.
  it('speaks the language of the jar it is showing', async () => {
    renderScreen(data([envelope({ id: 4, name: 'Зобовʼязання', kind: 'Debt', isDefault: false })], {
      recent: [
        {
          id: 12, date: '2026-07-30', kind: 'Deposit', amount: 800, amountOriginal: 800,
          currencyOriginal: 'PLN', note: null, envelopeId: 4, envelopeName: 'Зобовʼязання',
          isAuto: false, isTransfer: false,
        },
      ],
    }))
    const user = userEvent.setup()

    await user.click(screen.getByText('Зобовʼязання'))

    expect(screen.getByText('+ Погасити')).toBeInTheDocument()
    expect(screen.getByText('Погашення')).toBeInTheDocument()
    expect(screen.queryByText('Внесок')).not.toBeInTheDocument()
  })
  /// By hand this was two movements, and in between the money existed nowhere.
  it('moves money to another jar in one act', async () => {
    const onTransfer = vi.fn()
    renderScreen(data([
      envelope({ id: 1, name: 'Заощадження', balance: 1000 }),
      envelope({ id: 3, name: 'Відпустка', isDefault: false, balance: 0 }),
    ]), vi.fn(), { onTransfer })
    const user = userEvent.setup()

    await user.click(screen.getByText('Заощадження'))
    await user.click(screen.getByText('Перекинути в іншу банку'))
    await user.type(screen.getByLabelText('Скільки перекинути'), '400')
    await user.click(screen.getByText('Перекинути'))

    await waitFor(() => expect(onTransfer).toHaveBeenCalledWith({
      fromEnvelopeId: 1, toEnvelopeId: 3, amount: 400, currency: 'PLN',
    }))
  })

  /// An empty jar has nothing to transfer, and the only jar has nowhere to send it.
  it('does not offer a move from an empty jar', async () => {
    renderScreen(data([
      envelope({ id: 1, name: 'Заощадження', balance: 0 }),
      envelope({ id: 3, name: 'Відпустка', isDefault: false, balance: 0 }),
    ]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Заощадження'))

    expect(screen.queryByText('Перекинути в іншу банку')).not.toBeInTheDocument()
  })

  /// Half a transfer is not a movement in itself: editing it alone would put the two sides of
  /// one act out of step.
  it('shows a transfer as a transfer and does not open it for editing', async () => {
    renderScreen(data([envelope({ id: 1, name: 'Заощадження', balance: 600 })], {
      recent: [
        {
          id: 20, date: '2026-07-30', kind: 'Withdrawal', amount: 400, amountOriginal: 400,
          currencyOriginal: 'PLN', note: 'У «Відпустка»', envelopeId: 1, envelopeName: 'Заощадження',
          isAuto: false, isTransfer: true,
        },
      ],
    }))
    const user = userEvent.setup()

    await user.click(screen.getByText('Заощадження'))

    const row = screen.getByText('Перекинуто звідси')
    expect(row).toBeInTheDocument()
    expect(row.closest('button')).toBeDisabled()
  })
})
