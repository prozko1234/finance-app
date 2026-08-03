import { describe, expect, it } from 'vitest'
import { plural } from './format'

describe('plural', () => {
  const word = (n: number) => `${n} ${plural(n, 'запис', 'записи', 'записів')}`

  it('follows the Ukrainian rule for the ordinary cases', () => {
    expect(word(1)).toBe('1 запис')
    expect(word(2)).toBe('2 записи')
    expect(word(4)).toBe('4 записи')
    expect(word(5)).toBe('5 записів')
    expect(word(0)).toBe('0 записів')
  })

  it('handles the teens, where the last digit lies', () => {
    expect(word(11)).toBe('11 записів')
    expect(word(12)).toBe('12 записів')
    expect(word(14)).toBe('14 записів')
  })

  it('goes by the last digit again above twenty', () => {
    expect(word(21)).toBe('21 запис')
    expect(word(22)).toBe('22 записи')
    expect(word(25)).toBe('25 записів')
    expect(word(111)).toBe('111 записів')
  })
})
