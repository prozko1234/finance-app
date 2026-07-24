import { describe, it, expect } from 'vitest'
import { money } from './format'

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
