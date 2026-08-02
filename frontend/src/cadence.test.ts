import { describe, expect, it } from 'vitest'
import { cadenceLabel, scheduleSummary } from './cadence'

describe('cadenceLabel', () => {
  it('names the schedules the picker offers', () => {
    expect(cadenceLabel('Week', 2)).toBe('Раз на 2 тижні')
    expect(cadenceLabel('Month', 3)).toBe('Раз на квартал')
    expect(cadenceLabel('Year', 1)).toBe('Раз на рік')
  })

  it('still names a combination the picker cannot produce', () => {
    // The API accepts any interval, so a row could arrive with one the buttons do not cover.
    // Rendering it blank would be worse than rendering it plainly.
    expect(cadenceLabel('Month', 5)).toBe('Кожні 5 місяці')
  })
})

describe('scheduleSummary', () => {
  it('names a weekly charge by its weekday, not by a date', () => {
    // 2026-08-03 is a Monday.
    expect(scheduleSummary('Week', 1, '2026-08-03')).toBe('кожного понеділка')
  })

  /// Ukrainian weekdays are not all one gender, and the article has to follow: "кожної
  /// неділі" but "кожного понеділка". Assembling the phrase from one template got half of
  /// them wrong.
  it('agrees in gender with the weekday', () => {
    expect(scheduleSummary('Week', 1, '2026-08-02')).toBe('кожної неділі') // Sunday
    expect(scheduleSummary('Week', 1, '2026-08-05')).toBe('кожної середи') // Wednesday
    expect(scheduleSummary('Week', 1, '2026-08-06')).toBe('кожного четверга') // Thursday
  })

  it('keeps the weekday for a fortnightly charge', () => {
    expect(scheduleSummary('Week', 2, '2026-08-03')).toBe('кожного другого понеділка')
    expect(scheduleSummary('Week', 2, '2026-08-02')).toBe('кожної другої неділі')
  })

  it('names a monthly charge by its day of month', () => {
    expect(scheduleSummary('Month', 1, '2026-08-10')).toBe('кожного 10-го')
  })

  it('says both the cadence and the day when it is not every month', () => {
    expect(scheduleSummary('Month', 3, '2026-08-10')).toBe('раз на квартал, 10-го')
  })

  it('names a yearly charge by day and month', () => {
    // Genitive month — "10 серпня", not the "серпень" Intl gives for a standalone month.
    expect(scheduleSummary('Year', 1, '2026-08-10')).toBe('щороку 10 серпня')
  })

  it('falls back to the cadence when the date is unusable', () => {
    expect(scheduleSummary('Month', 1, 'not a date')).toBe('Щомісяця')
  })
})
