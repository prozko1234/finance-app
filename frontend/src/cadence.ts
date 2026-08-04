import type { RecurrenceUnit } from './types'

/// The model takes any "every N units"; the screen offers the seven that people actually
/// have. A free number box plus a unit dropdown would be more powerful and worse: nobody
/// pays for something every 5 weeks, and two controls is two decisions where one will do.
export interface Cadence {
  unit: RecurrenceUnit
  interval: number
  label: string
}

export const CADENCES: Cadence[] = [
  { unit: 'Week', interval: 1, label: 'Щотижня' },
  { unit: 'Week', interval: 2, label: 'Раз на 2 тижні' },
  { unit: 'Month', interval: 1, label: 'Щомісяця' },
  { unit: 'Month', interval: 2, label: 'Раз на 2 місяці' },
  { unit: 'Month', interval: 3, label: 'Раз на квартал' },
  { unit: 'Month', interval: 6, label: 'Раз на пів року' },
  { unit: 'Year', interval: 1, label: 'Раз на рік' },
]

export const DEFAULT_CADENCE = CADENCES[2]

/// What one charge costs per month, whatever rhythm it actually runs on. A yearly domain and
/// a weekly cleaner are the same kind of standing cost, and the only way to compare them — or
/// to answer "скільки в мене йде на підписки" — is to put them on one scale.
///
/// Weeks are converted through the average year (365.25 / 7 / 12 ≈ 4.348 weeks per month), not
/// through "4 weeks": that would under-count a weekly charge by a whole month's worth a year.
export function perMonth(amount: number, unit: RecurrenceUnit, interval: number): number {
  const safe = Math.max(interval, 1)
  if (unit === 'Week') return (amount * 365.25) / 7 / 12 / safe
  if (unit === 'Year') return amount / 12 / safe
  return amount / safe
}

export function sameCadence(a: { unit: RecurrenceUnit; interval: number }, b: Cadence): boolean {
  return a.unit === b.unit && a.interval === b.interval
}

/// Names a schedule. Falls back to a generic phrase for combinations the picker cannot
/// produce but the API accepts — a row edited elsewhere must never render as blank.
export function cadenceLabel(unit: RecurrenceUnit, interval: number): string {
  const known = CADENCES.find((c) => sameCadence({ unit, interval }, c))
  if (known) return known.label

  const plural = { Week: 'тижні', Month: 'місяці', Year: 'роки' }[unit]
  return `Кожні ${interval} ${plural}`
}

/// Written out per weekday rather than assembled from parts: Ukrainian weekdays are not all
/// the same gender, so "кожного" and "кожної" alternate — неділя is feminine, понеділок is
/// not, and a single template gets one of them wrong every time.
const WEEKLY = [
  'кожної неділі', 'кожного понеділка', 'кожного вівторка', 'кожної середи',
  'кожного четверга', "кожної п'ятниці", 'кожної суботи',
]

const FORTNIGHTLY = [
  'кожної другої неділі', 'кожного другого понеділка', 'кожного другого вівторка',
  'кожної другої середи', 'кожного другого четверга', "кожної другої п'ятниці",
  'кожної другої суботи',
]

/// Genitive too — "10 серпня", not the "серпень" that Intl returns for a standalone month.
const MONTHS = [
  'січня', 'лютого', 'березня', 'квітня', 'травня', 'червня',
  'липня', 'серпня', 'вересня', 'жовтня', 'листопада', 'грудня',
]

/// The one line under a subscription that says when it comes back. Weekly schedules are
/// named by weekday and monthly ones by day of month, because that is how each is
/// remembered — "кожного вівторка" and "кожного 10-го", never the other way round.
export function scheduleSummary(unit: RecurrenceUnit, interval: number, startsOn: string): string {
  const date = new Date(startsOn)
  if (Number.isNaN(date.getTime())) return cadenceLabel(unit, interval)

  if (unit === 'Week') {
    return (interval === 1 ? WEEKLY : FORTNIGHTLY)[date.getDay()]
  }

  if (unit === 'Year') {
    return `щороку ${date.getDate()} ${MONTHS[date.getMonth()]}`
  }

  const day = date.getDate()
  return interval === 1
    ? `кожного ${day}-го`
    : `${cadenceLabel(unit, interval).toLowerCase()}, ${day}-го`
}
