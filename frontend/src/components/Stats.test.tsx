import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Stats } from './Stats'
import type { Stats as StatsData } from '../types'

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
      { categoryId: 1, name: 'Їжа', icon: '🍕', amount: 3000, percent: 75, count: 12 },
      { categoryId: 2, name: 'Розваги', icon: null, amount: 1000, percent: 25, count: 3 },
    ],
    ...over,
  }
}

const props = { selected: null, onSelectMonth: vi.fn(), onBack: vi.fn() }

describe('Stats', () => {
  it('shows a month in the red as a loss, not as a bare number', () => {
    render(<Stats {...props} data={data()} />)

    expect(screen.getByText(/-1500,00/)).toBeInTheDocument()
    expect(screen.getByText(/\+8000,00/)).toBeInTheDocument()
  })

  it('breaks the selected month down by category, biggest first', () => {
    render(<Stats {...props} data={data()} />)

    const names = screen.getAllByText(/Їжа|Розваги/).map((e) => e.textContent)
    expect(names[0]).toContain('Їжа')
    expect(screen.getByText(/75%/)).toBeInTheDocument()
  })

  it('tapping a month asks for that month', async () => {
    const onSelectMonth = vi.fn()
    render(<Stats {...props} data={data()} onSelectMonth={onSelectMonth} />)

    // The month is named twice on this screen now — the bars first, the savings card after.
    await userEvent.click(screen.getAllByText(/черв/i)[0])

    expect(onSelectMonth).toHaveBeenCalledWith('2026-06')
  })

  it('says a month was empty instead of showing an empty card', () => {
    render(<Stats {...props} data={data({ categories: [], selectedExpense: 0 })} />)

    expect(screen.getByText(/витрат ще не було/i)).toBeInTheDocument()
  })

  /// The question the screen was extended for: how much stays put, and how much of that the
  /// scheme does on its own versus what still takes a decision.
  it('reports what stayed in the jars, split into the plan and the rest', () => {
    render(<Stats {...props} data={data()} />)

    // 2400 + 600 by plan and hand in July, less the 500 taken back out in June.
    expect(screen.getByText('2500,00 zł')).toBeInTheDocument()
    expect(screen.getByText(/13% доходу за 2 міс/)).toBeInTheDocument()
    expect(screen.getByText(/2400,00 zł за схемою · 600,00 zł руками/)).toBeInTheDocument()
  })

  it('keeps the way back while the numbers are still loading', () => {
    render(<Stats {...props} data={null} />)

    expect(screen.getByLabelText('Назад')).toBeInTheDocument()
  })
})
