import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Allocation } from './Allocation'
import type { Allocation as AllocationData } from '../types'

function data(over: Partial<AllocationData> = {}): AllocationData {
  return {
    active: {
      name: 'Тільки денна норма',
      preset: 'daily-norm-only',
      buckets: [{ name: 'На витрати', kind: 'Spending', percent: 100 }],
    },
    presets: [
      {
        key: 'daily-norm-only', name: 'Тільки денна норма', hint: 'Весь бюджет — на витрати',
        buckets: [{ name: 'На витрати', kind: 'Spending', percent: 100 }],
      },
      {
        key: '50-30-20', name: '50/30/20', hint: 'Потреби / бажання / заощадження',
        buckets: [
          { name: 'Потреби', kind: 'Spending', percent: 50 },
          { name: 'Бажання', kind: 'Spending', percent: 30 },
          { name: 'Заощадження', kind: 'Savings', percent: 20 },
        ],
      },
    ],
    ...over,
  }
}

const props = { budget: 6000, currency: 'PLN', onBack: vi.fn() }

describe('Allocation', () => {
  it('shows what each preset means in money, not only percentages', () => {
    render(<Allocation {...props} data={data()} onSave={vi.fn()} />)

    expect(screen.getByText(/3000,00/)).toBeInTheDocument() // 50% of 6000
    expect(screen.getByText(/1200,00/)).toBeInTheDocument() // 20% savings
  })

  it('applies a preset with a single tap', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined)
    render(<Allocation {...props} data={data()} onSave={onSave} />)

    await userEvent.click(screen.getByText('50/30/20'))

    expect(onSave).toHaveBeenCalledWith({ preset: '50-30-20' })
  })

  /// "А гроші, які вже відкладені?" comes up the moment the tap lands, and the answer belongs
  /// on the screen rather than in the author's head.
  it('says what the change did to the period that is running', async () => {
    const onSave = vi.fn().mockResolvedValue(undefined)
    const { rerender } = render(<Allocation {...props} data={data()} onSave={onSave} />)

    await userEvent.click(screen.getByText('50/30/20'))

    // The server confirmed it: the active scheme is a different one now.
    rerender(
      <Allocation
        {...props}
        onSave={onSave}
        data={data({
          active: {
            name: '50/30/20',
            preset: '50-30-20',
            buckets: [
              { name: 'Потреби', kind: 'Spending', percent: 50 },
              { name: 'Бажання', kind: 'Spending', percent: 30 },
              { name: 'Заощадження', kind: 'Savings', percent: 20 },
            ],
          },
        })}
      />,
    )

    expect(screen.getByText(/Цей період перерахували/)).toBeInTheDocument()
    // The jar is named, not just totalled: the bucket IS the jar the app will create and fill.
    expect(screen.getByText(/«Заощадження» 1200,00/)).toBeInTheDocument()
    expect(screen.getByText(/Минулі періоди лишились як були/)).toBeInTheDocument()
  })

  it('refuses to save a custom split that does not add up to 100', async () => {
    render(<Allocation {...props} data={data()} onSave={vi.fn()} />)

    await userEvent.click(screen.getByText(/Свій розподіл/))
    await userEvent.clear(screen.getByLabelText('Частка кошика Заощадження'))
    await userEvent.type(screen.getByLabelText('Частка кошика Заощадження'), '30')

    expect(screen.getByText(/має бути 100%/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Застосувати свій розподіл/ })).toBeDisabled()
  })
})
