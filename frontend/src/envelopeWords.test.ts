import { describe, it, expect } from 'vitest'
import { envelopeIcon, envelopeWords } from './envelopeWords'

describe('envelopeWords', () => {
  /// У банці «Зобовʼязання» «внесок у заощадження» під 🐖 читався як помилка застосунку:
  /// гроші, що йдуть на борг, не відкладаються в скарбничку.
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
