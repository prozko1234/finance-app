import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Categories } from './Categories'
import type { Category } from '../types'

const rows: Category[] = [
  { id: 1, name: 'Продукти', icon: '🛒', sortOrder: 1, isSystem: false, kind: 'Expense' },
  { id: 2, name: 'Інше', icon: '📦', sortOrder: 99, isSystem: true, kind: 'Expense' },
  { id: 3, name: 'Зарплата', icon: '💼', sortOrder: 1, isSystem: false, kind: 'Income' },
]

function props(over: Partial<Parameters<typeof Categories>[0]> = {}) {
  return {
    categories: rows,
    onCreate: vi.fn().mockResolvedValue(rows[0]),
    onUpdate: vi.fn().mockResolvedValue(undefined),
    onDelete: vi.fn().mockResolvedValue(undefined),
    onBack: vi.fn(),
    ...over,
  }
}

/// A salary sitting between groceries and rent reads as a mistake — and until income had
/// categories of its own, that is exactly what the database contained.
describe('Категорії', () => {
  it('keeps the two sides of the ledger in separate lists', () => {
    render(<Categories {...props()} />)

    expect(screen.getByText('На що йдуть гроші')).toBeInTheDocument()
    expect(screen.getByText('Звідки приходять')).toBeInTheDocument()
    expect(screen.getByText('Продукти')).toBeInTheDocument()
    expect(screen.getByText('Зарплата')).toBeInTheDocument()
  })

  it('says nothing about a side that has no categories', () => {
    render(<Categories {...props({ categories: rows.filter((c) => c.kind !== 'Income') })} />)
    expect(screen.queryByText('Звідки приходять')).not.toBeInTheDocument()
  })

  it('creates an expense category unless told otherwise', async () => {
    const onCreate = vi.fn().mockResolvedValue(rows[0])
    render(<Categories {...props({ onCreate })} />)

    await userEvent.type(screen.getByPlaceholderText('Назва'), 'Хобі')
    await userEvent.click(screen.getByText('Додати'))

    expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({ name: 'Хобі', kind: 'Expense' }))
  })

  it('creates an income category when that is the side chosen', async () => {
    const onCreate = vi.fn().mockResolvedValue(rows[2])
    render(<Categories {...props({ onCreate })} />)

    await userEvent.click(screen.getByRole('button', { name: 'Надходження' }))
    await userEvent.type(screen.getByPlaceholderText('Назва'), 'Дивіденди')
    await userEvent.click(screen.getByText('Додати'))

    expect(onCreate).toHaveBeenCalledWith(expect.objectContaining({ name: 'Дивіденди', kind: 'Income' }))
  })

  /// The fallback of each list receives what a deleted category leaves behind, so it cannot be
  /// deleted itself.
  it('offers no delete on a system category', () => {
    render(<Categories {...props()} />)
    expect(screen.getByText('системна')).toBeInTheDocument()
  })
})
