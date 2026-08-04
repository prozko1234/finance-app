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
          <Saved data={data} />
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

/// «Скільки я відкладаю» — and, right beside it, the answer to the question that always comes
/// next: how the allocation scheme and the jars relate to each other.
///
/// They are two halves of one number. The scheme moves its share into jars by itself at the
/// start of every period (that is «за схемою»); anything the user adds on top, takes back out,
/// or pays straight out of a jar is «руками». Summed, that is what actually stayed put — and
/// that sum, not the plan, is what the rate is computed from. A scheme promising 20% while
/// the jars get raided every month should read as a low number, not a high one.
///
/// The rate is against income rather than against the budget: income is the figure the user
/// recognises without having to reconstruct anything.
function Saved({ data }: { data: StatsData }) {
  const c = data.currency
  const months = data.months.filter((m) => m.income > 0 || m.savedByPlan !== 0 || m.savedByHand !== 0)
  if (months.length === 0) return null

  const total = months.reduce((s, m) => s + m.savedByPlan + m.savedByHand, 0)
  const income = months.reduce((s, m) => s + m.income, 0)
  const byPlan = months.reduce((s, m) => s + m.savedByPlan, 0)

  return (
    <Card>
      <div className="flex items-baseline justify-between gap-3">
        <SectionTitle>Скільки лишається в банках</SectionTitle>
        <span className={`text-sm font-medium tabular-nums ${total < 0 ? 'text-red-600' : ''}`}>
          {money(total, c)}
        </span>
      </div>

      <p className="text-sm text-neutral-500">
        {income > 0
          ? `Це ${rate(total, income)} доходу за ${months.length} міс.`
          : `За ${months.length} міс.`}
        {byPlan !== 0 && ` · ${money(byPlan, c)} схема відклала сама`}
      </p>

      <div className="space-y-1.5">
        {months.map((m) => {
          const saved = m.savedByPlan + m.savedByHand
          return (
            <div key={m.month} className="flex items-baseline justify-between gap-3 text-sm">
              <span className="text-neutral-500">{monthLabel(m.month)}</span>
              <span className="tabular-nums text-right">
                <span className={saved < 0 ? 'text-red-600' : 'text-emerald-600'}>
                  {saved > 0 ? '+' : ''}{money(saved, c)}
                </span>
                {m.income > 0 && <span className="text-neutral-400"> · {rate(saved, m.income)}</span>}
                {/* Both halves only when they differ — «500 за схемою» under a row that
                    already says 500 is a line to read for nothing. */}
                {m.savedByHand !== 0 && m.savedByPlan !== 0 && (
                  <span className="block text-xs text-neutral-400">
                    {money(m.savedByPlan, c)} за схемою · {money(m.savedByHand, c)} руками
                  </span>
                )}
              </span>
            </div>
          )
        })}
      </div>

      <p className="text-xs text-neutral-400">
        Схема розподілу кладе свою частку в банки сама, на початку кожного періоду — це «за
        схемою». «Руками» — те, що ти доклав понад план, зняв або заплатив прямо з банки. Разом
        вони і є те, що справді лишилось: план, з якого щомісяця знімають, тут покаже мало.
      </p>
    </Card>
  )
}

/// Share as a whole percent. Rounded because a savings rate with a decimal point invites
/// reading a rounding artefact as a change.
function rate(part: number, whole: number): string {
  if (whole <= 0) return '—'
  return `${Math.round((part / whole) * 100)}%`
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
