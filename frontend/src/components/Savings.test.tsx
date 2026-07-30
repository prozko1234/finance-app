import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Savings } from './Savings'
import type { EnvelopeSummary, SaveEnvelope, SaveEnvelopeTarget, Savings as SavingsData } from '../types'

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
  }> = {},
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
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
        {...envelopeHandlers}
        onBack={vi.fn()}
      />
    </QueryClientProvider>,
  )
}

describe('Savings', () => {
  /// Раніше екран відкривався на одній банці, і де лежить решта — було невидно.
  it('opens on the list of every envelope with its balance', () => {
    renderScreen(data([envelope(), envelope({ id: 2, name: 'Пенсія', balance: 4200, isDefault: false })]))

    expect(screen.getByText('Відкладено всього')).toBeInTheDocument()
    expect(screen.getByText(/12 400,00/)).toBeInTheDocument() // 8200 + 4200
    expect(screen.getByText('Заощадження')).toBeInTheDocument()
    expect(screen.getByText('Пенсія')).toBeInTheDocument()
  })

  /// Те, заради чого екран переробляли: за період видно і рух, і що стало з балансом.
  it('shows period by period once an envelope is opened', async () => {
    renderScreen(data([envelope()]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Заощадження'))

    expect(screen.getByText('По періодах')).toBeInTheDocument()
    expect(screen.getByText('10 липня – 9 серпня')).toBeInTheDocument()
    expect(screen.getByText('+1200,00 zł')).toBeInTheDocument()
    // Двічі на екрані: великою цифрою зверху і як баланс після цього періоду.
    expect(screen.getAllByText('8200,00 zł').length).toBeGreaterThan(1)
    // Зняття видно так само чесно, як і відкладання.
    expect(screen.getByText('−400,00 zł')).toBeInTheDocument()
  })

  /// План активний, а ціль 0 — без причини на екрані це виглядає як зламаний додаток.
  it('says why nothing is put aside in a period that started from a count', () => {
    renderScreen(data([envelope({ monthGoal: 0, depositedThisMonth: 0 })], {
      monthGoal: 0, depositedThisMonth: 0, planPausedFrom: '2026-07-20',
    }))

    expect(screen.getByText(/20 липня ти порахував залишок/)).toBeInTheDocument()
  })

  /// Внесок за схемою можна було відредагувати чи видалити — і наступне завантаження
  /// екрана приводило його назад. Дія, яка ніби спрацювала і скасувалась сама.
  it('does not let the scheme own deposit be edited or deleted', async () => {
    const onDeleteEntry = vi.fn()
    renderScreen(data([envelope()], {
      recent: [
        {
          id: 10, date: '2026-07-30', kind: 'Deposit', amount: 1200, amountOriginal: 1200,
          currencyOriginal: 'PLN', note: 'За схемою «70/20/10»', envelopeId: 1, envelopeName: 'Заощадження',
          isAuto: true,
        },
        {
          id: 11, date: '2026-07-30', kind: 'Deposit', amount: 200, amountOriginal: 200,
          currencyOriginal: 'PLN', note: 'понад план', envelopeId: 1, envelopeName: 'Заощадження',
          isAuto: false,
        },
      ],
    }), onDeleteEntry)
    const user = userEvent.setup()

    await user.click(screen.getByText('Заощадження'))

    // Рух руками лишається з ✕; за схемою — без нього.
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
  // Банки як самостійна річ: до цього банку можна було отримати лише як кошик схеми.

  /// Слово «банка» саме запрошує зробити банку на відпустку — а зробити її було неможливо.
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

  /// Банка, що зникла з грошима всередині, забрала б їх із «Відкладено всього» — тобто з
  /// тієї єдиної цифри, якій застосунок просить вірити.
  it('does not offer to put away a pot that still holds money', async () => {
    renderScreen(data([envelope({ id: 3, name: 'Відпустка', isDefault: false, balance: 240 })]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Відпустка'))

    expect(screen.queryByText('Прибрати банку')).not.toBeInTheDocument()
    expect(screen.getByText(/у ній ще 240,00/)).toBeInTheDocument()
  })

  /// Назву банки зі схеми шукає кошик — перейменування тихо віддало б баланс банці, яку
  /// ніхто не наповнює.
  it('does not offer renaming for a pot the scheme owns', async () => {
    renderScreen(data([envelope({ id: 2, name: 'Пенсія', isDefault: false, isFromScheme: true })]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Пенсія'))

    expect(screen.queryByText('Назва й вид')).not.toBeInTheDocument()
    expect(screen.getByText(/задає схема розподілу/)).toBeInTheDocument()
  })
  // Ціль на банку: без неї банка, яку не годує схема, — скарбничка без сенсу.

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
    // Найважливіше вголос: ціль не забирає нічого з денної норми.
    expect(screen.getByText(/нічого не тримає з «Можна витратити сьогодні»/)).toBeInTheDocument()
  })

  /// Дата необовʼязкова: «зібрати 6 000» — теж ціль, і вигадувати за людину дедлайн не можна.
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
    // Без дати темпу немає — і екран каже це, а не показує 0 за період.
    expect(screen.getByText(/Дати немає, тож і темпу немає/)).toBeInTheDocument()

    await user.click(screen.getByText('Прибрати'))
    await waitFor(() => expect(onSetTarget).toHaveBeenCalledWith(3, { amount: null }))
  })
  /// «Внесок у заощадження» під 🐖 у банці «Зобовʼязання» читався як помилка застосунку.
  it('speaks the language of the jar it is showing', async () => {
    renderScreen(data([envelope({ id: 4, name: 'Зобовʼязання', kind: 'Debt', isDefault: false })], {
      recent: [
        {
          id: 12, date: '2026-07-30', kind: 'Deposit', amount: 800, amountOriginal: 800,
          currencyOriginal: 'PLN', note: null, envelopeId: 4, envelopeName: 'Зобовʼязання',
          isAuto: false,
        },
      ],
    }))
    const user = userEvent.setup()

    await user.click(screen.getByText('Зобовʼязання'))

    expect(screen.getByText('+ Погасити')).toBeInTheDocument()
    expect(screen.getByText('Погашення')).toBeInTheDocument()
    expect(screen.queryByText('Внесок')).not.toBeInTheDocument()
  })
})
