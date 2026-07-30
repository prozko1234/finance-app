import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Balance } from './Balance'
import type { OpeningBalance } from '../types'

function data(over: Partial<OpeningBalance> = {}): OpeningBalance {
  return { isSet: true, amount: 1800, currency: 'PLN', date: '2026-07-25', appliesNow: true, ...over }
}

const props = { currency: 'PLN', onBack: vi.fn() }

describe('Balance', () => {
  /// Головна причина існування екрана: цифру, що керує денною нормою, можна було ввести
  /// лише в онбордингу — тобто один раз у житті застосунку.
  it('says what applies now and offers to count again', () => {
    render(<Balance {...props} data={data()} onSet={vi.fn()} onClear={vi.fn()} />)

    expect(screen.getByText('Діє зараз')).toBeInTheDocument()
    expect(screen.getByText(/1800,00/)).toBeInTheDocument()
    expect(screen.getByText(/Порахував 25 липня/)).toBeInTheDocument()
    expect(screen.getByText('Порахувати заново')).toBeInTheDocument()
  })

  it('lets a wrong figure be taken back out', async () => {
    const onClear = vi.fn().mockResolvedValue(undefined)
    render(<Balance {...props} data={data()} onSet={vi.fn()} onClear={onClear} />)

    await userEvent.click(screen.getByText(/Прибрати/))

    await waitFor(() => expect(onClear).toHaveBeenCalledTimes(1))
  })

  /// Порахований минулого періоду залишок уже не керує нормою — і не має пропонувати
  /// «прибрати», бо прибирати нічого.
  it('does not offer to clear a count that no longer applies', () => {
    render(<Balance {...props} data={data({ appliesNow: false })} onSet={vi.fn()} onClear={vi.fn()} />)

    expect(screen.getByText('Уже не діє')).toBeInTheDocument()
    expect(screen.queryByText(/Прибрати/)).not.toBeInTheDocument()
  })

  it('sends the amount with the day it was counted on', async () => {
    const onSet = vi.fn().mockResolvedValue(undefined)
    render(<Balance {...props} data={data({ isSet: false, amount: null, date: null })} onSet={onSet} onClear={vi.fn()} />)

    await userEvent.type(screen.getByLabelText('Сума на руках'), '2400')
    await userEvent.click(screen.getByText(/Це в мене зараз/))

    await waitFor(() => expect(onSet).toHaveBeenCalledTimes(1))
    const [payload] = onSet.mock.calls[0]
    expect(payload.amount).toBe(2400)
    expect(payload.currency).toBe('PLN')
    expect(payload.date).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })

  it('says the budget comes from income while nothing is counted', () => {
    render(<Balance {...props} data={data({ isSet: false, amount: null, date: null })} onSet={vi.fn()} onClear={vi.fn()} />)

    expect(screen.getByText('Зараз не задано')).toBeInTheDocument()
  })
})
