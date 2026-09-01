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
    startsOn: '2026-08-05', unit: 'Month', interval: 1,
    active: true, note: 'Netflix', kind: 'Expense', amountIncludesVat: true,
    nextChargeOn: null, chargedThisPeriod: false,
    ...over,
  }
}

/// Mocks stay typed as mocks (not as the plain prop signatures) so the assertions
/// can read `.mock.calls` without casting.
function props(over: {
  items?: RecurringType[]
  onCreate?: Mock
  onUpdate?: Mock
  onConfirmCharge?: Mock
  onUnconfirmCharge?: Mock
  onSkipCharge?: Mock
} = {}) {
  return {
    categories,
    items: over.items ?? [],
    onCreate: over.onCreate ?? vi.fn<(r: SaveRecurring) => Promise<void>>().mockResolvedValue(undefined),
    onUpdate: over.onUpdate ?? vi.fn<(id: number, r: SaveRecurring) => Promise<void>>().mockResolvedValue(undefined),
    onToggle: vi.fn(),
    onDelete: vi.fn<(id: number) => Promise<void>>().mockResolvedValue(undefined),
    onConfirmCharge: over.onConfirmCharge ?? vi.fn(),
    onUnconfirmCharge: over.onUnconfirmCharge ?? vi.fn(),
    onSkipCharge: over.onSkipCharge ?? vi.fn(),
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
  /// This screen used to be the only place in the app with a mark-then-confirm pair: a
  /// transaction went on one tap, a subscription took two. One pattern now — deleted at once,
  /// with the app-wide undo bar to take it back (see undo.ts).
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
  /// A subscription could only be deleted and typed in again, even though `PUT` had existed
  /// all along — there was just no way to call it.
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
      amount: 65, startsOn: '2026-08-05', unit: 'Month', interval: 1, note: 'Netflix', categoryId: 1,
    })))
  })

  /// The pause and the kind (income or expense) are not in this form, so correcting an
  /// amount must not quietly change them.
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
    // The list is what the screen goes back to, not the add form: with rows on it, the form
    // is somewhere you go on purpose.
    expect(screen.getByText(/\+ Додати підписку/)).toBeInTheDocument()
  })
})

describe('Recurring — the monthly cost', () => {
  it('adds every rhythm up onto one monthly figure', () => {
    render(<Recurring {...props({ items: [
      item({ id: 1, amountOriginal: 50, unit: 'Month', interval: 1 }),
      item({ id: 2, amountOriginal: 1200, unit: 'Year', interval: 1 }),
      item({ id: 3, amountOriginal: 999, active: false }),
    ] })} />)

    // 50 a month + 1200 a year = 150 a month; the paused row costs nothing.
    expect(screen.getByText('−150,00 zł')).toBeInTheDocument()
    expect(screen.getByText(/2 активних · 1 призупинено/)).toBeInTheDocument()
  })

  it('opens the add form only when asked, once there is a list to read', async () => {
    const user = userEvent.setup()
    render(<Recurring {...props({ items: [item()] })} />)

    expect(screen.queryByPlaceholderText('0')).not.toBeInTheDocument()
    await user.click(screen.getByText(/\+ Додати підписку/))
    expect(screen.getByPlaceholderText('0')).toBeInTheDocument()
  })
})

describe('Recurring — when it next goes out', () => {
  /// "Кожного 5-го" is equally true the day before the charge and the day after, and the
  /// money has already gone by then.
  it('says when the next charge lands and whether this period already paid', () => {
    render(<Recurring {...props({ items: [
      item({ id: 1, note: 'Netflix', nextChargeOn: '2099-01-05', chargedThisPeriod: true }),
      item({ id: 2, note: 'Оренда', nextChargeOn: '2099-01-10', chargedThisPeriod: false }),
      item({ id: 3, note: 'Spotify', active: false, nextChargeOn: null }),
    ] })} />)

    // The row itself says only WHEN. What this period did with it is the status line below,
    // which is the one that can be acted on.
    expect(screen.getAllByText(/5 січня/).length).toBeGreaterThan(0)
    expect(screen.getByText(/на паузі/)).toBeInTheDocument()
    expect(screen.getByText(/^Оплачено/)).toBeInTheDocument()
  })
})

/// The field used to be a date picker labelled «Перше списання» for every rhythm, and it read
/// as a one-off: the app has always charged «кожного 10-го», but nothing on the form said so.
describe('when the charge lands', () => {
  it('asks a monthly rule for a day of the month, not a date', async () => {
    const user = userEvent.setup()
    render(<Recurring {...props({ items: [item()] })} />)
    await user.click(screen.getByText(/Netflix/))

    const day = screen.getByLabelText('День місяця')
    expect(day).toHaveValue('5')
    expect(screen.queryByLabelText('Перше списання')).not.toBeInTheDocument()

    await user.selectOptions(day, '20')
    expect(screen.getByText(/Списуватиметься кожного 20-го/)).toBeInTheDocument()
  })

  /// A week has no day of the month and a year's month has to come from somewhere, so those
  /// keep the date. The weekday is read off it.
  it('keeps the date for a weekly rule', async () => {
    const user = userEvent.setup()
    render(<Recurring {...props({ items: [item({ unit: 'Week' })] })} />)
    await user.click(screen.getByText(/Netflix/))

    expect(screen.getByLabelText('Перше списання')).toBeInTheDocument()
    expect(screen.queryByLabelText('День місяця')).not.toBeInTheDocument()
  })

  it('warns that a late day falls back to the end of a short month', async () => {
    const user = userEvent.setup()
    render(<Recurring {...props({ items: [item()] })} />)
    await user.click(screen.getByText(/Netflix/))

    await user.selectOptions(screen.getByLabelText('День місяця'), '31')
    expect(screen.getByText(/У коротких місяцях — останнього дня/)).toBeInTheDocument()
  })
})

/// Changing the day or the price throws away the charge already written for this period and
/// writes it again from the new rule. That is right, and invisible unless it is said out loud.
describe('editing a rule that has already charged', () => {
  it('says what will happen to the unconfirmed charge', async () => {
    const user = userEvent.setup()
    render(<Recurring {...props({ items: [item()] })} />)
    await user.click(screen.getByText(/Netflix/))

    expect(screen.queryByText(/ще не підтверджене, приберемо/)).not.toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('День місяця'), '20')
    expect(screen.getByText(/День зміниться/)).toBeInTheDocument()
    expect(screen.getByText(/ще не підтверджене, приберемо/)).toBeInTheDocument()
  })

  it('says the same about a price change', async () => {
    const user = userEvent.setup()
    render(<Recurring {...props({ items: [item()] })} />)
    await user.click(screen.getByText(/Netflix/))

    await user.clear(screen.getByPlaceholderText('0'))
    await user.type(screen.getByPlaceholderText('0'), '75')

    expect(screen.getByText(/Сума зміниться/)).toBeInTheDocument()
  })
})

/// Three states, not two. A charge whose day has passed but which nobody has confirmed used
/// to read exactly like one still ahead — so a subscription could look like it was still
/// coming while its money was already being held.
describe('what the row says about this period', () => {
  it('names a charge waiting to be confirmed', () => {
    render(<Recurring {...props({ items: [item({ awaitingConfirmation: true, chargeId: 9, nextChargeOn: '2026-09-05' })] })} />)
    expect(screen.getByText(/Чекає підтвердження/)).toBeInTheDocument()
  })

  it('still says when a confirmed charge has gone', () => {
    render(<Recurring {...props({ items: [item({ chargedThisPeriod: true, chargeId: 9, nextChargeOn: '2026-09-05' })] })} />)
    expect(screen.getByText(/^Оплачено/)).toBeInTheDocument()
  })
})

/// «Які підписки вже сплачені, які ні» — a question the app held the answer to and showed
/// nowhere. The home screen only ever asks about charges whose day has passed, and only until
/// they are answered, so one answered by mistake had no way back at all.
describe('Recurring — статус за період', () => {
  const netflix = (over: Partial<RecurringType> = {}): RecurringType => ({
    id: 1, amountOriginal: 45.99, currencyOriginal: 'PLN', categoryId: 1,
    categoryName: 'Підписки', startsOn: '2026-01-05', unit: 'Month', interval: 1,
    active: true, note: 'Netflix', kind: 'Expense', amountIncludesVat: false,
    nextChargeOn: '2026-09-05', chargedThisPeriod: false, ...over,
  })

  it('asks about a charge whose day has passed, and confirms it', async () => {
    const onConfirmCharge = vi.fn()
    const user = userEvent.setup()
    render(<Recurring {...props({
      items: [netflix({ awaitingConfirmation: true, chargeId: 77, chargeOn: '2026-08-05' })],
      onConfirmCharge,
    })} />)

    expect(screen.getByText(/Чекає підтвердження/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Оплачено ✓' }))

    expect(onConfirmCharge).toHaveBeenCalledWith(77)
  })

  /// The tick is one tap on a card that appears unbidden at the top of the home screen, so it
  /// gets mis-tapped. Deleting the charge is not the way back: that says it never happened.
  it('takes a confirmation back', async () => {
    const onUnconfirmCharge = vi.fn()
    const user = userEvent.setup()
    render(<Recurring {...props({
      items: [netflix({ chargedThisPeriod: true, chargeId: 77, chargeOn: '2026-08-05' })],
      onUnconfirmCharge,
    })} />)

    expect(screen.getByText(/^Оплачено/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Скасувати' }))

    expect(onUnconfirmCharge).toHaveBeenCalledWith(77)
  })

  it('says when nothing has been taken yet this period', () => {
    render(<Recurring {...props({ items: [netflix()] })} />)

    expect(screen.getByText(/Цього періоду ще не списувалось/)).toBeInTheDocument()
  })

  /// The count first, because that is what "які вже пішли" actually asks; the money follows
  /// for the part still standing between the user and the end of the period.
  it('counts what is paid and what is still to come', () => {
    render(<Recurring {...props({
      items: [
        netflix({ id: 1, chargedThisPeriod: true, chargeId: 1 }),
        netflix({ id: 2, note: 'Spotify', amountOriginal: 28.56 }),
        netflix({ id: 3, note: 'Google One', amountOriginal: 8.99 }),
      ],
    })} />)

    expect(screen.getByText('1 з 3')).toBeInTheDocument()
    expect(screen.getByText(/Ще має піти.*37,55/)).toBeInTheDocument()
  })

  /// A paused rule has no occurrence this period, and income is not something one "pays".
  it('says nothing about a paused subscription', () => {
    render(<Recurring {...props({ items: [netflix({ active: false })] })} />)

    expect(screen.queryByText(/Цього періоду ще не списувалось/)).not.toBeInTheDocument()
  })
})
