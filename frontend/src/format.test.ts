import { describe, it, expect } from 'vitest'
import { dayHeading, daysUntil, money } from './format'

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

  /// Дата в кожному рядку списку нічого не додавала — у заголовку дня вона потрібна раз.
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

/// Локальні копії помічників із types.ts — тест про календар не має тягти сюди типи застосунку.
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
