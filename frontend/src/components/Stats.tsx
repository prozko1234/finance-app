import type { Stats as StatsData } from '../types'
import { money } from '../format'
import { Card, CardSkeleton, Screen, SectionTitle } from './Screen'

/// Two questions, one screen: «чи я виходжу в плюс» (a row per month, income against
/// expense) and «на що пішло» (that month's expenses by category). No date pickers, no
/// filters, no chart library — bars are divs, and a wider question is a wider screen.
///
/// Bars are drawn with paired horizontal rows rather than vertical columns because on a
/// phone a column chart of six months is either unreadable or scrolls sideways, and
/// comparing two numbers within a month is the whole point.

const MONTHS_BACK = 6

interface Props {
  data: StatsData | null
  selected: string | null
  onSelectMonth: (month: string) => void
  onBack: () => void
}

export function Stats({ data, selected, onSelectMonth, onBack }: Props) {
  return (
    <Screen
      title="Статистика"
      onBack={onBack}
      subtitle="Останні пів року: скільки прийшло, скільки пішло і куди саме. Місяці тут календарні — з 1-го по останнє число, а не від зарплати до зарплати, як на головній."
      footnote={data
        ? `Кожен місяць переведено в ${data.currency} за курсом свого кінця місяця — тому вже закритий місяць не змінюється щодня.`
        : undefined}
    >
      {!data ? <CardSkeleton /> : (
        <>
          <MonthBars data={data} selected={selected ?? data.selectedMonth} onSelect={onSelectMonth} />
          <Categories data={data} />
        </>
      )}
    </Screen>
  )
}

function MonthBars({ data, selected, onSelect }: {
  data: StatsData; selected: string; onSelect: (month: string) => void
}) {
  // One scale for every month, or a quiet month would look like a busy one.
  const scale = Math.max(...data.months.flatMap((m) => [m.income, m.expense]), 1)

  return (
    <Card>
      <SectionTitle>Доходи і витрати</SectionTitle>
      <div className="space-y-2">
        {data.months.map((m) => {
          const active = m.month === selected
          return (
            <button
              key={m.month}
              onClick={() => onSelect(m.month)}
              aria-current={active ? 'true' : undefined}
              className={`w-full rounded-xl px-3 py-2 text-left ${
                active ? 'bg-neutral-100 dark:bg-neutral-800' : 'hover:bg-neutral-50 dark:hover:bg-neutral-800/50'
              }`}
            >
              <div className="flex items-baseline justify-between text-sm">
                <span className={active ? 'font-medium' : 'text-neutral-500'}>{monthLabel(m.month)}</span>
                <span className={m.net < 0 ? 'text-red-600' : 'text-emerald-600'}>
                  {m.net > 0 ? '+' : ''}{money(m.net, data.currency)}
                </span>
              </div>
              <Bar value={m.income} scale={scale} className="bg-emerald-500" />
              <Bar value={m.expense} scale={scale} className="bg-neutral-400 dark:bg-neutral-500" />
            </button>
          )
        })}
      </div>
      <p className="text-xs text-neutral-400">
        <span className="text-emerald-600">▬</span> дохід · <span className="text-neutral-400">▬</span> витрати ·
        {' '}торкнись місяця, щоб побачити його категорії
      </p>
    </Card>
  )
}

/// A zero-width bar is invisible, and an invisible bar reads as missing data — so an
/// empty month keeps a hairline.
function Bar({ value, scale, className }: { value: number; scale: number; className: string }) {
  return (
    <div className="mt-1 h-2 rounded-full bg-neutral-100 dark:bg-neutral-800">
      <div
        className={`h-2 rounded-full ${className}`}
        style={{ width: `${Math.max((value / scale) * 100, value > 0 ? 2 : 0)}%` }}
      />
    </div>
  )
}

function Categories({ data }: { data: StatsData }) {
  return (
    <Card>
      <div className="flex items-baseline justify-between">
        <SectionTitle>Куди пішло — {monthLabel(data.selectedMonth)}</SectionTitle>
        <span className="text-sm font-medium">{money(data.selectedExpense, data.currency)}</span>
      </div>

      {data.categories.length === 0 ? (
        <p className="text-sm text-neutral-500">Цього місяця витрат ще не було.</p>
      ) : (
        <div className="space-y-3">
          {data.categories.map((c) => (
            <div key={c.categoryId}>
              <div className="flex items-baseline justify-between text-sm">
                <span>{c.icon ? `${c.icon} ` : ''}{c.name}</span>
                <span className="tabular-nums">
                  {money(c.amount, data.currency)}
                  <span className="text-neutral-400"> · {c.percent}%</span>
                </span>
              </div>
              <div className="mt-1 h-2 rounded-full bg-neutral-100 dark:bg-neutral-800">
                <div className="h-2 rounded-full bg-neutral-900 dark:bg-white" style={{ width: `${c.percent}%` }} />
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  )
}

/// "2026-07" → "лип 26". Parsed by hand: `new Date('2026-07')` is UTC-midnight, which in a
/// negative-offset zone is the previous month.
function monthLabel(month: string): string {
  const [year, m] = month.split('-').map(Number)
  if (!year || !m) return month
  return new Intl.DateTimeFormat('uk-UA', { month: 'short', year: '2-digit' })
    .format(new Date(year, m - 1, 1))
}

export { MONTHS_BACK }
