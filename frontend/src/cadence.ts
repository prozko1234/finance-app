import type { Recurring, RecurrenceUnit } from './types'

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

export interface MonthlyTotal {
  currency: string
  expense: number
  income: number
}

/// What a set of standing charges comes to in a month, one entry per currency they were
/// entered in. NOT converted to a single currency: a rate would have to be fetched, and a
/// total in a currency none of the rows are in reads as an app's opinion rather than an
/// answer. Paused rows are left out — they cost nothing while paused.
///
/// Lives here rather than in either screen that shows it, because both the subscription list
/// and the statistics screen answer "скільки в мене йде на підписки" and two copies of this
/// arithmetic would eventually disagree.
export function monthlyTotals(items: Recurring[]): MonthlyTotal[] {
  const totals = new Map<string, MonthlyTotal>()

  for (const r of items.filter((x) => x.active)) {
    const row = totals.get(r.currencyOriginal)
      ?? { currency: r.currencyOriginal, expense: 0, income: 0 }
    const share = perMonth(r.amountOriginal, r.unit, r.interval)
    if (r.kind === 'Income') row.income += share
    else row.expense += share
    totals.set(r.currencyOriginal, row)
  }

  return [...totals.values()]
}

/// The model stores a full date — the first charge — because a weekly rule has no day of the
/// month at all. A monthly one is remembered as a day and nothing else ("кожного 10-го"), so
/// that is what the form asks for; these two turn one into the other.
export function dayOfMonth(iso: string): number {
  return Number(iso.slice(8, 10)) || 1
}

/// The same anchor moved onto another day of the month, walking back to a month that actually
/// HAS that day rather than clamping into a short one. Anchoring the 31st on a 28-day February
/// is what pins a series to the 28th for good: every occurrence is counted from the anchor, so
/// the anchor itself has to be a real 31st.
export function withDayOfMonth(iso: string, day: number): string {
  const [year, month] = iso.split('-').map(Number)
  if (!year || !month) return iso

  let y = year
  let m = month
  for (let i = 0; i < 12 && day > daysIn(y, m); i++) {
    m -= 1
    if (m === 0) { m = 12; y -= 1 }
  }

  return `${y}-${pad(m)}-${pad(day)}`
}

/// Day 0 of the next month is the last day of this one — and built from local parts, so it is
/// not the UTC-midnight trap that turns a date into the previous day west of Greenwich.
function daysIn(year: number, month: number): number {
  return new Date(year, month, 0).getDate()
}

function pad(v: number): string {
  return String(v).padStart(2, '0')
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
