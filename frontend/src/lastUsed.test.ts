import { beforeEach, describe, expect, it } from 'vitest'
import { readIncomeSources, rememberIncomeSource } from './lastUsed'

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
