import type { RecurrenceUnit } from '../types'
import { dayOfMonth, scheduleSummary, withDayOfMonth } from '../cadence'

/// When a recurring charge lands, asked the way the rhythm is actually remembered.
///
/// The field used to be a date picker labelled «Перше списання» for every rhythm, and it read
/// as a one-off: the app has always charged «кожного 10-го», but nothing on the form said so,
/// so setting up a monthly subscription looked like scheduling a single payment on a date.
///
/// A month has a day and nothing else — that is the whole answer, and picking it out of a
/// calendar means choosing a year nobody is thinking about. A week does not have one at all,
/// and neither does a year: for those the date stays, because the weekday and the month are
/// read off it.
export function ChargeDay({ unit, interval, value, onChange }: {
  unit: RecurrenceUnit
  interval: number
  /// The anchor as the model stores it — a full ISO date, whichever control is shown.
  value: string
  onChange: (iso: string) => void
}) {
  const byDay = unit === 'Month'

  return (
    <div className="space-y-1">
      <div className="flex items-center gap-2 text-sm">
        <span className="text-neutral-500 shrink-0">{byDay ? 'Кожного' : 'Перше списання'}</span>
        {byDay ? (
          <select
            value={dayOfMonth(value)}
            onChange={(e) => onChange(withDayOfMonth(value, Number(e.target.value)))}
            aria-label="День місяця"
            className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5"
          >
            {DAYS.map((d) => <option key={d} value={d}>{d}-го</option>)}
          </select>
        ) : (
          <input
            type="date"
            value={value}
            onChange={(e) => onChange(e.target.value)}
            aria-label="Перше списання"
            className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5"
          />
        )}
      </div>

      {/* The schedule in words: "13 серпня" on its own does not say whether that is a Tuesday,
          nor whether it comes back every week. */}
      <p className="text-xs text-neutral-400">
        Списуватиметься {scheduleSummary(unit, interval, value)}.
        {byDay && dayOfMonth(value) > 28 && ' У коротких місяцях — останнього дня.'}
      </p>
    </div>
  )
}

const DAYS = Array.from({ length: 31 }, (_, i) => i + 1)
