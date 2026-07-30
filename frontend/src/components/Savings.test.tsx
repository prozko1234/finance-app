import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Savings } from './Savings'
import type { EnvelopeSummary, Savings as SavingsData } from '../types'

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

function renderScreen(d: SavingsData, onDeleteEntry: (id: number) => Promise<void> = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <Savings
        data={d}
        onSavePlan={vi.fn()}
        onAddEntry={vi.fn()}
        onUpdateEntry={vi.fn()}
        onDeleteEntry={onDeleteEntry}
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
})
