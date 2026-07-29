import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
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

function data(envelopes: EnvelopeSummary[]): SavingsData {
  return {
    mode: 'Percent', value: 20, active: true,
    balance: 8200, monthGoal: 1200, depositedThisMonth: 1200, stillToReserve: 0,
    currency: 'PLN', recent: [], envelopes, goalFromScheme: '70/20/10',
  }
}

function renderScreen(d: SavingsData) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <Savings
        data={d}
        onSavePlan={vi.fn()}
        onAddEntry={vi.fn()}
        onUpdateEntry={vi.fn()}
        onDeleteEntry={vi.fn()}
        onBack={vi.fn()}
      />
    </QueryClientProvider>,
  )
}

describe('Savings', () => {
  /// Раніше екран відкривався на одному конверті, і де лежить решта — було невидно.
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

  it('does not offer the savings plan on an envelope the scheme drives', async () => {
    renderScreen(data([envelope({ id: 2, name: 'Пенсія', isDefault: false })]))
    const user = userEvent.setup()

    await user.click(screen.getByText('Пенсія'))

    expect(screen.queryByText(/Відкладати щомісяця/)).not.toBeInTheDocument()
  })
})
