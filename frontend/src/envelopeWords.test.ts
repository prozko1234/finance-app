import { describe, it, expect } from 'vitest'
import { envelopeIcon, envelopeWords } from './envelopeWords'

describe('envelopeWords', () => {
  /// In a jar called "Зобовʼязання", "внесок у заощадження" under a 🐖 read as a bug: money
  /// going towards a debt is not being saved into a piggy bank.
  it('names the movement after what the jar actually is', () => {
    expect(envelopeWords('Debt').deposit).toBe('Погашення')
    expect(envelopeWords('Debt').depositAction).toBe('Погасити')
    expect(envelopeWords('Investing').deposit).toBe('Інвестовано')
    expect(envelopeWords('Savings').deposit).toBe('Внесок')
  })

  it('gives every kind its own icon instead of a piggy bank on all of them', () => {
    const icons = (['Savings', 'Investing', 'Debt', 'Other'] as const).map(envelopeIcon)
    expect(new Set(icons).size).toBe(icons.length)
  })
})
