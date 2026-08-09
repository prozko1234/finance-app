import { describe, it, expect } from 'vitest'
import { dayHeading, daysUntil, money, parseAmount } from './format'

describe('money', () => {
  it('formats PLN in pl-PL locale', () => {
    const s = money(345.25, 'PLN')
    expect(s).toContain('345,25')
    expect(s).toContain('zł')
  })

  it('falls back to number + code for an invalid currency', () => {
    const s = money(10, 'XX')
    expect(s).toContain('10.00')
    expect(s).toContain('XX')
  })
})

describe('dayHeading', () => {
  const today = new Date(2026, 6, 30) // 30 липня 2026

  /// A date on every row of the list added nothing — the day's heading needs it once.
  it('names the day the way a person would', () => {
    expect(dayHeading('2026-07-30', today)).toBe('Сьогодні')
    expect(dayHeading('2026-07-29', today)).toBe('Вчора')
    expect(dayHeading('2026-07-24', today)).toBe('24 липня')
  })
})

describe('daysUntil', () => {
  it('counts whole days, not hours', () => {
    const iso = todayIsoLocal()
    expect(daysUntil(iso)).toBe(0)
    expect(daysUntil(shiftIsoLocal(iso, 6))).toBe(6)
  })
})

/// Local copies of the helpers from types.ts — a test about the calendar should not drag the
/// app's types in with it.
function todayIsoLocal(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

function shiftIsoLocal(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(y, m - 1, d + days)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${dt.getFullYear()}-${pad(dt.getMonth() + 1)}-${pad(dt.getDate())}`
}

describe('parseAmount', () => {
  it('reads a plain number', () => {
    expect(parseAmount('1930')).toBe(1930)
  })

  /// The reported case: the button would not light up for «1 930», because Number() reads a
  /// space as "not a number" — and the app's own formatter prints thousands with one.
  it('reads a number with thousands separated by a space', () => {
    expect(parseAmount('1 930')).toBe(1930)
    expect(parseAmount('1 930')).toBe(1930)
    expect(parseAmount('1 930')).toBe(1930)
  })

  it('accepts either decimal separator', () => {
    expect(parseAmount('12,50')).toBe(12.5)
    expect(parseAmount('12.50')).toBe(12.5)
  })

  /// An empty field is not a zero: a form that read it as one would offer to put 0 aside.
  it('is not a number when there is nothing to read', () => {
    expect(parseAmount('')).toBeNaN()
    expect(parseAmount('   ')).toBeNaN()
    expect(parseAmount('абв')).toBeNaN()
  })
})
