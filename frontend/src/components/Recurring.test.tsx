import { describe, it, expect, vi, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Recurring } from './Recurring'
import type { Category, Recurring as RecurringType, SaveRecurring } from '../types'

const categories: Category[] = [
  { id: 1, name: 'Підписки', icon: '📺', sortOrder: 1, isSystem: false },
  { id: 2, name: 'Житло', icon: '🏠', sortOrder: 2, isSystem: false },
]

function item(over: Partial<RecurringType> = {}): RecurringType {
  return {
    id: 1, amountOriginal: 50, currencyOriginal: 'PLN', categoryId: 1, categoryName: 'Підписки',
    dayOfMonth: 5, active: true, note: 'Netflix', kind: 'Expense', amountIncludesVat: true,
    nextChargeOn: null, chargedThisPeriod: false,
    ...over,
  }
}

/// Mocks stay typed as mocks (not as the plain prop signatures) so the assertions
/// can read `.mock.calls` without casting.
function props(over: { items?: RecurringType[]; onCreate?: Mock; onUpdate?: Mock } = {}) {
  return {
    categories,
    items: over.items ?? [],
    onCreate: over.onCreate ?? vi.fn<(r: SaveRecurring) => Promise<void>>().mockResolvedValue(undefined),
    onUpdate: over.onUpdate ?? vi.fn<(id: number, r: SaveRecurring) => Promise<void>>().mockResolvedValue(undefined),
    onToggle: vi.fn(),
    onDelete: vi.fn<(id: number) => Promise<void>>().mockResolvedValue(undefined),
    onBack: vi.fn(),
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

describe('Recurring — delete', () => {
  /// Пара «познач і підтверди» жила тут одна на весь застосунок: транзакцію видаляв один
  /// тап без питань, а підписку — два з підтвердженням. Тепер патерн один — видаляємо
  /// одразу, а повернути дає панель «Повернути» рівнем вище (див. undo.ts).
  it('deletes on the first tap and leaves the undo to the app', async () => {
    const user = userEvent.setup()
    const p = props({ items: [item(), item({ id: 2, note: 'Оренда', categoryId: 2 })] })
    render(<Recurring {...p} />)

    await user.click(screen.getAllByLabelText('Видалити')[0])

    await waitFor(() => expect(p.onDelete).toHaveBeenCalledTimes(1))
    expect(p.onDelete).toHaveBeenCalledWith(1)
    expect(screen.queryByText(/Видалити 1\?/)).not.toBeInTheDocument()
  })
})

describe('Recurring — editing', () => {
  /// Підписку можна було лише видалити й ввести заново — при тому що `PUT` існував весь час,
  /// не було лише способу його покликати.
  it('opens the same form on the row and saves the correction', async () => {
    const user = userEvent.setup()
    const onUpdate = vi.fn<(id: number, r: SaveRecurring) => Promise<void>>().mockResolvedValue(undefined)
    render(<Recurring {...props({ items: [item({ id: 9, amountOriginal: 50, note: 'Netflix' })], onUpdate })} />)

    await user.click(screen.getByText('Netflix'))
    expect(screen.getByText('Редагуємо «Netflix»')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('0')).toHaveValue('50')

    await user.clear(screen.getByPlaceholderText('0'))
    await user.type(screen.getByPlaceholderText('0'), '65')
    await user.click(screen.getByText('Зберегти зміни'))

    await waitFor(() => expect(onUpdate).toHaveBeenCalledWith(9, expect.objectContaining({
      amount: 65, dayOfMonth: 5, note: 'Netflix', categoryId: 1,
    })))
  })

  /// Пауза й вид (дохід чи витрата) не в цій формі — правка суми не має їх мовчки міняти.
  it('leaves the pause and the kind exactly as they were', async () => {
    const user = userEvent.setup()
    const onUpdate = vi.fn<(id: number, r: SaveRecurring) => Promise<void>>().mockResolvedValue(undefined)
    const paused = item({ id: 4, active: false, kind: 'Income', note: 'Зарплата', amountOriginal: 20000 })
    render(<Recurring {...props({ items: [paused], onUpdate })} />)

    await user.click(screen.getByText('Зарплата'))
    await user.clear(screen.getByPlaceholderText('0'))
    await user.type(screen.getByPlaceholderText('0'), '21000')
    await user.click(screen.getByText('Зберегти зміни'))

    await waitFor(() => expect(onUpdate).toHaveBeenCalledWith(4, expect.objectContaining({
      amount: 21000, active: false, kind: 'Income',
    })))
  })

  it('goes back to adding when the correction is cancelled', async () => {
    const user = userEvent.setup()
    render(<Recurring {...props({ items: [item({ note: 'Netflix' })] })} />)

    await user.click(screen.getByText('Netflix'))
    await user.click(screen.getByText('Скасувати'))

    expect(screen.queryByText(/Редагуємо/)).not.toBeInTheDocument()
    expect(screen.getByText('+ Ще одна')).toBeInTheDocument()
  })
})

describe('Recurring — when it next goes out', () => {
  /// «Кожного 5-го» однаково правдиве і за день до списання, і за день після — а гроші тим
  /// часом уже пішли.
  it('says when the next charge lands and whether this period already paid', () => {
    render(<Recurring {...props({ items: [
      item({ id: 1, note: 'Netflix', nextChargeOn: '2099-01-05', chargedThisPeriod: true }),
      item({ id: 2, note: 'Оренда', nextChargeOn: '2099-01-10', chargedThisPeriod: false }),
      item({ id: 3, note: 'Spotify', active: false, nextChargeOn: null }),
    ] })} />)

    expect(screen.getByText(/цього періоду вже пішло/)).toBeInTheDocument()
    expect(screen.getByText(/5 січня/)).toBeInTheDocument()
    expect(screen.getByText(/на паузі/)).toBeInTheDocument()
  })
})
