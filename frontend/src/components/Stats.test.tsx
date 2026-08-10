import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Stats } from './Stats'
import type { RecentSpending, Recurring, Stats as StatsData } from '../types'

function data(over: Partial<StatsData> = {}): StatsData {
  return {
    currency: 'PLN',
    months: [
      { month: '2026-06', income: 8000, expense: 9500, net: -1500, savedByPlan: 0, savedByHand: -500 },
      { month: '2026-07', income: 12000, expense: 4000, net: 8000, savedByPlan: 2400, savedByHand: 600 },
    ],
    selectedMonth: '2026-07',
    selectedExpense: 4000,
    categories: [
      { categoryId: 1, name: 'Їжа', icon: '🍕', amount: 3000, percent: 75, count: 12, typical: 2500 },
      { categoryId: 2, name: 'Розваги', icon: null, amount: 1000, percent: 25, count: 3, typical: 1000 },
    ],
    savedBalance: 2500,
    savedByCurrency: null,
    ...over,
  }
}

function subscription(over: Partial<Recurring> = {}): Recurring {
  return {
    id: 1, amountOriginal: 60, currencyOriginal: 'PLN', categoryId: 1, categoryName: 'Інтернет',
    startsOn: '2026-01-10', unit: 'Month', interval: 1, active: true, note: null, kind: 'Expense',
    amountIncludesVat: false, nextChargeOn: '2026-08-10', chargedThisPeriod: false, ...over,
  }
}

function recent(over: Partial<RecentSpending> = {}): RecentSpending {
  return {
    currency: 'PLN', days: 7, total: 470, previousTotal: 300,
    categories: [
      { categoryId: 2, name: 'Таксі', icon: '🚕', amount: 380, count: 2, previousAmount: 240 },
      { categoryId: 1, name: 'Кава', icon: '☕', amount: 90, count: 6, previousAmount: 60 },
    ],
    ...over,
  }
}

const props = {
  selected: null, recurring: [], onSelectMonth: vi.fn(), onBack: vi.fn(),
  recent: recent(), recentDays: 7, onRecentDays: vi.fn(),
}

/// The half-year cards answer "як було", not "що робити", so the screen no longer opens on
/// them. Tests that read them have to say so out loud, the same way the user does.
async function openHistory() {
  await userEvent.click(screen.getByText(/Історія за пів року/))
}

describe('Stats', () => {
  it('shows a month in the red as a loss, not as a bare number', async () => {
    render(<Stats {...props} data={data()} />)
    openHistory()

    await openHistory()
    expect(screen.getByText(/-1500,00/)).toBeInTheDocument()
    expect(screen.getByText(/\+8000,00/)).toBeInTheDocument()
  })

  it('breaks the selected month down by category, biggest first', async () => {
    render(<Stats {...props} data={data()} />)

    const names = screen.getAllByText(/Їжа|Розваги/).map((e) => e.textContent)
    await openHistory()
    expect(names[0]).toContain('Їжа')
    expect(screen.getByText(/75%/)).toBeInTheDocument()
  })

  it('tapping a month asks for that month', async () => {
    const onSelectMonth = vi.fn()
    render(<Stats {...props} data={data()} onSelectMonth={onSelectMonth} />)

    await openHistory()

    // The month is named twice on this screen now — the bars first, the savings card after.
    await userEvent.click(screen.getAllByText(/черв/i)[0])

    expect(onSelectMonth).toHaveBeenCalledWith('2026-06')
  })

  it('says a month was empty instead of showing an empty card', async () => {
    render(<Stats {...props} data={data({ categories: [], selectedExpense: 0 })} />)

    await openHistory()
    expect(screen.getByText(/витрат ще не було/i)).toBeInTheDocument()
  })

  /// The question the screen was extended for: how much stays put, and how much of that the
  /// scheme does on its own versus what still takes a decision.
  it('reports what stayed in the jars, split into the plan and the rest', async () => {
    render(<Stats {...props} data={data()} />)

    // 2400 + 600 by plan and hand in July, less the 500 taken back out in June.
    await openHistory()
    expect(screen.getByText(/Відкладено за 2 міс.: 2500,00 zł/)).toBeInTheDocument()
    expect(screen.getByText(/13% доходу/)).toBeInTheDocument()
    expect(screen.getByText(/2400,00 zł за схемою · 600,00 zł руками/)).toBeInTheDocument()
  })

  /// Flow and stock are different questions. The months say what MOVED, and money recorded as
  /// already set aside is deliberately not movement — so without the balance beside them the
  /// screen adds up to less than the jars hold and reads as though something went missing.
  it('shows what the jars hold now, not only what moved', async () => {
    render(<Stats {...props} data={data({ savedBalance: 9500 })} />)

    await openHistory()
    expect(screen.getByText('9500,00 zł')).toBeInTheDocument()
    expect(screen.getByText(/Відкладено за 2 міс.: 2500,00 zł/)).toBeInTheDocument()
  })

  /// The niche the whole app exists for: money living in more than one currency. One converted
  /// figure hides both what is held and the fact that half of it moves with the rate.
  it('keeps each currency separate when the jars are not all in one', async () => {
    render(<Stats {...props} data={data({
      savedByCurrency: [
        { currency: 'PLN', amount: 7000 },
        { currency: 'USD', amount: 500 },
      ],
    })} />)

    await openHistory()
    expect(screen.getByText(/7000,00 zł · 500,00/)).toBeInTheDocument()
    expect(screen.getByText(/без перерахунку/)).toBeInTheDocument()
  })

  it('says nothing about currencies when there is only one', () => {
    render(<Stats {...props} data={data()} />)

    expect(screen.queryByText(/без перерахунку/)).not.toBeInTheDocument()
  })

  /// The reason the screen exists: the largest category is rent every month and says nothing.
  /// The one that grew against ITSELF is the only line that can be acted on today.
  it('names the category that went over its own normal, not the largest one', () => {
    render(<Stats {...props} data={data()} />)

    expect(screen.getByText(/проти звичних 2500,00/)).toBeInTheDocument()
    // Розваги landed exactly on its normal, so it is not called an overrun.
    const overruns = screen.getByText(/Що вилізло за межу/).closest('div')!
    expect(overruns.textContent).not.toContain('Розваги')
  })

  it('says nothing is unusual rather than showing an empty card', () => {
    render(<Stats {...props} data={data({
      categories: [
        { categoryId: 1, name: 'Їжа', icon: '🍕', amount: 2550, percent: 100, count: 12, typical: 2500 },
      ],
    })} />)

    expect(screen.getByText(/Нічого незвичного/)).toBeInTheDocument()
  })

  /// A first month has nothing to be typical against, and telling a new user that everything
  /// they spend is an overrun is the fastest way to lose them.
  it('drops the comparison entirely when there is no history', () => {
    render(<Stats {...props} data={data({
      categories: [
        { categoryId: 1, name: 'Їжа', icon: '🍕', amount: 3000, percent: 100, count: 12, typical: null },
      ],
    })} />)

    expect(screen.queryByText(/Що вилізло за межу/)).not.toBeInTheDocument()
    expect(screen.queryByText(/звичних/)).not.toBeInTheDocument()
  })

  /// Thirty small buys is a habit and three big ones is a decision — the same total, undone
  /// in opposite ways, and the bar cannot tell them apart.
  it('says how many purchases a category is made of, and their average', async () => {
    render(<Stats {...props} data={data()} />)

    await openHistory()
    expect(screen.getByText('12 × сер. 250,00 zł')).toBeInTheDocument()
  })

  /// The half of "що можна оптимізувати" that needs no willpower: cancelled once, saves every
  /// month after.
  it('totals the standing charges and names the dearest', () => {
    render(<Stats {...props} recurring={[
      subscription({ id: 1, amountOriginal: 60, categoryName: 'Інтернет' }),
      subscription({ id: 2, amountOriginal: 1200, unit: 'Year', categoryName: 'Домен' }),
      subscription({ id: 3, amountOriginal: 500, active: false, categoryName: 'Спортзал' }),
    ]} data={data()} />)

    // 60/міс + 1200/рік = 160 a month; the paused gym is not counted.
    expect(screen.getByText('160,00 zł')).toBeInTheDocument()
    expect(screen.getByText(/Найдорожчий — Домен/)).toBeInTheDocument()
  })

  it('keeps the way back while the numbers are still loading', () => {
    render(<Stats {...props} data={null} />)

    expect(screen.getByLabelText('Назад')).toBeInTheDocument()
  })
})

/// Money, not counts. The home screen's shortcut row already ranks recent categories by how
/// OFTEN they are used, and that answers a different question — thirty coffees and one taxi
/// look nothing alike in a list of counts and identical in a wallet.
describe('останній тиждень', () => {
  it('leads with what cost the most, and says how it compares', () => {
    render(<Stats {...props} data={data()} />)

    expect(screen.getByText(/Куди пішло за 7 днів/)).toBeInTheDocument()
    expect(screen.getByText(/470,00/)).toBeInTheDocument()
    expect(screen.getByText(/Попередні 7 днів — 300,00/)).toBeInTheDocument()
    expect(screen.getByText(/Таксі/)).toBeInTheDocument()
  })

  it('asks for a fortnight when a fortnight is asked for', async () => {
    const onRecentDays = vi.fn()
    render(<Stats {...props} data={data()} onRecentDays={onRecentDays} />)

    await userEvent.click(screen.getByRole('button', { name: '14' }))
    expect(onRecentDays).toHaveBeenCalledWith(14)
  })

  /// A category first used this week is not "+100%", it is new — so there is nothing to say.
  it('stays silent about a category with nothing behind it', () => {
    render(
      <Stats
        {...props}
        data={data()}
        recent={recent({
          total: 380, previousTotal: 0,
          categories: [{ categoryId: 2, name: 'Таксі', icon: '🚕', amount: 380, count: 2, previousAmount: 0 }],
        })}
      />,
    )

    expect(screen.queryByText(/Попередні 7 днів/)).not.toBeInTheDocument()
  })

  it('says a quiet week was quiet', () => {
    render(<Stats {...props} data={data()} recent={recent({ total: 0, previousTotal: 0, categories: [] })} />)
    expect(screen.getByText(/За цей час витрат не було/)).toBeInTheDocument()
  })
})
