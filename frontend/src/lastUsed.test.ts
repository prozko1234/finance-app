import { beforeEach, describe, expect, it } from 'vitest'
import { readHorizon, readIncomeSources, rememberIncomeSource, writeLastUsed } from './lastUsed'

/// The scale the home card is read at survives a reload. It is the first thing seen on every
/// open, and being handed back "День" after choosing "Тиждень" is the app forgetting the one
/// preference it has.
describe('the day/week/period choice', () => {
  beforeEach(() => localStorage.clear())

  it('defaults to the day when nothing was ever chosen', () => {
    expect(readHorizon()).toBe('day')
  })

  it('comes back as it was left', () => {
    writeLastUsed({ horizon: 'week' })

    expect(readHorizon()).toBe('week')
  })

  /// Written alongside the entry form's own defaults, not over them — one key holds both, and
  /// picking a horizon must not wipe the income sources.
  it('does not disturb the other remembered defaults', () => {
    rememberIncomeSource('ACME')
    writeLastUsed({ horizon: 'period' })

    expect(readHorizon()).toBe('period')
    expect(readIncomeSources()).toEqual(['ACME'])
  })

  it('falls back to the day when the stored value is nonsense', () => {
    localStorage.setItem('finance:lastUsed', '{"horizon":"decade"}')

    expect(readHorizon()).toBe('day')
  })
})

describe('income sources', () => {
  beforeEach(() => localStorage.clear())

  it('keeps the most recent source first', () => {
    rememberIncomeSource('ACME')
    rememberIncomeSource('Faktura Nexus')

    expect(readIncomeSources()).toEqual(['Faktura Nexus', 'ACME'])
  })

  it('moves a repeated source up instead of duplicating it', () => {
    rememberIncomeSource('ACME')
    rememberIncomeSource('Nexus')
    rememberIncomeSource('acme')

    expect(readIncomeSources()).toEqual(['acme', 'Nexus'])
  })

  it('ignores blank input', () => {
    rememberIncomeSource('   ')

    expect(readIncomeSources()).toEqual([])
  })

  it('keeps at most five — a shortcut, not a search', () => {
    for (const s of ['a', 'b', 'c', 'd', 'e', 'f']) rememberIncomeSource(s)

    expect(readIncomeSources()).toEqual(['f', 'e', 'd', 'c', 'b'])
  })
})
