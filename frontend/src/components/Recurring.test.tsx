import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Recurring } from './Recurring'
import type { Category, Recurring as RecurringType } from '../types'

const categories: Category[] = [
  { id: 1, name: 'Підписки', icon: '📺', isDefault: true },
  { id: 2, name: 'Житло', icon: '🏠', isDefault: true },
]

function item(over: Partial<RecurringType> = {}): RecurringType {
  return {
    id: 1, amountOriginal: 50, currencyOriginal: 'PLN', categoryId: 1, categoryName: 'Підписки',
    dayOfMonth: 5, active: true, note: 'Netflix', kind: 'Expense', amountIncludesVat: true,
    ...over,
  }
}

function props(over: Partial<Parameters<typeof Recurring>[0]> = {}) {
  return {
    categories, items: [], onCreate: vi.fn().mockResolvedValue(undefined), onToggle: vi.fn(),
    onDelete: vi.fn().mockResolvedValue(undefined), onBack: vi.fn(),
    ...over,
  }
}

async function fillRow(user: ReturnType<typeof userEvent.setup>, amount: string, name: string) {
  await user.clear(screen.getByPlaceholderText('0'))
  await user.type(screen.getByPlaceholderText('0'), amount)
  await user.type(screen.getByPlaceholderText(/Назва/), name)
}

describe('Recurring — batch add', () => {
  it('saves every queued row with one tap', async () => {
    const user = userEvent.setup()
    const p = props()
    render(<Recurring {...p} />)

    await fillRow(user, '50', 'Netflix')
    await user.click(screen.getByText('+ Ще одна'))
    await fillRow(user, '30', 'Spotify')

    // The row still in the form counts too — it must not need a second tap.
    await user.click(screen.getByText('Зберегти (2)'))

    await waitFor(() => expect(p.onCreate).toHaveBeenCalledTimes(2))
    expect(p.onCreate.mock.calls.map(([r]) => r.note)).toEqual(['Netflix', 'Spotify'])
  })

  it('keeps only the failed rows queued, so a retry does not duplicate', async () => {
    const user = userEvent.setup()
    const onCreate = vi.fn()
      .mockResolvedValueOnce(undefined)
      .mockRejectedValueOnce(new Error('Сервер недоступний'))
    render(<Recurring {...props({ onCreate })} />)

    await fillRow(user, '50', 'Netflix')
    await user.click(screen.getByText('+ Ще одна'))
    await fillRow(user, '30', 'Spotify')
    await user.click(screen.getByText('+ Ще одна'))
    await user.click(screen.getByText('Зберегти (2)'))

    expect(await screen.findByText('Сервер недоступний')).toBeInTheDocument()
    expect(screen.queryByText('Netflix')).not.toBeInTheDocument()
    expect(screen.getByText('Spotify')).toBeInTheDocument()
  })

  it('drops a queued row on ✕ without saving it', async () => {
    const user = userEvent.setup()
    const p = props()
    render(<Recurring {...p} />)

    await fillRow(user, '50', 'Netflix')
    await user.click(screen.getByText('+ Ще одна'))
    await user.click(screen.getByLabelText('Прибрати з черги'))
    expect(screen.queryByText('Netflix')).not.toBeInTheDocument()
    expect(p.onCreate).not.toHaveBeenCalled()
  })
})

describe('Recurring — batch delete', () => {
  it('deletes marked items only after confirming', async () => {
    const user = userEvent.setup()
    const p = props({ items: [item(), item({ id: 2, note: 'Оренда', categoryId: 2 })] })
    render(<Recurring {...p} />)

    await user.click(screen.getAllByLabelText('Позначити на видалення')[0])
    await user.click(screen.getAllByLabelText('Позначити на видалення')[0])
    expect(p.onDelete).not.toHaveBeenCalled()

    await user.click(screen.getByText('Видалити'))
    await waitFor(() => expect(p.onDelete).toHaveBeenCalledTimes(2))
    expect(p.onDelete.mock.calls.map(([id]) => id)).toEqual([1, 2])
  })

  it('unmarks everything on cancel', async () => {
    const user = userEvent.setup()
    const p = props({ items: [item()] })
    render(<Recurring {...p} />)

    await user.click(screen.getByLabelText('Позначити на видалення'))
    await user.click(screen.getByText('Скасувати'))

    expect(screen.queryByText(/Видалити 1/)).not.toBeInTheDocument()
    expect(p.onDelete).not.toHaveBeenCalled()
  })
})
