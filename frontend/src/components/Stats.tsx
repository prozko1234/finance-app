import { useState } from 'react'
import type {
  CategoryStats, RecentSpending, Recurring, SpendWindow, Stats as StatsData,
} from '../types'
import { dayMonth, money, plural } from '../format'
import { monthlyTotals, perMonth } from '../cadence'
import { Card, CardSkeleton, Screen, SectionTitle } from './Screen'

/// Statistics in two layers, because the two things people come here for are not the same size.
///
/// The top layer is one sentence and one number: how this week is going against the last one.
/// That is the whole of what a glance can carry, it is the only thing on the screen that can
/// still be acted on today, and it is what every app worth copying leads with — Copilot's
/// "spent so far vs typical", Emma's "you're up 18% on last week". Under it, the two cards that
/// name something to DO: what has run over its own normal, and what is being paid every month
/// without being decided again.
///
/// The bottom layer is history — half a year of income against expense, what stayed in the
/// jars, and one month by category. It cannot be acted on today and it used to sit between the
/// user and the cards that can, so it lives behind one button.
///
/// No date pickers, no filters, no chart library: bars are divs, and a wider question is a
/// wider screen. Bars are paired horizontal rows rather than vertical columns because on a
/// phone a column chart of six months is either unreadable or scrolls sideways, and comparing
/// two numbers within a month is the whole point.

const MONTHS_BACK = 6

interface Props {
  data: StatsData | null
  /// This week or this month so far, in money, against the same stretch one step back. Null
  /// while it loads.
  recent: RecentSpending | null
  window: SpendWindow
  onWindow: (w: SpendWindow) => void
  /// Standing charges, for the one card that answers "що можна скасувати". Empty while the
  /// list is still loading — the card simply does not appear yet.
  recurring: Recurring[]
  selected: string | null
  onSelectMonth: (month: string) => void
  onBack: () => void
}

export function Stats({
  data, recurring, recent, window, onWindow, selected, onSelectMonth, onBack,
}: Props) {
  // Half a year of totals is a thing you look at a few times a year; this week is a thing you
  // look at on a Sunday. Until this split the screen opened on the half-year and the actionable
  // half was three scrolls down.
  const [history, setHistory] = useState(false)

  return (
    <Screen
      title="Статистика"
      onBack={onBack}
      footnote={data
        ? `Місяці в історії переведено в ${data.currency} за курсом свого кінця місяця — тому вже закритий місяць не змінюється щодня.`
        : undefined}
    >
      {!data ? <CardSkeleton /> : (
        <>
          <RecentCard recent={recent} window={window} onWindow={onWindow} />
          <Overruns data={data} />
          <Subscriptions items={recurring} />

          {/* Everything below answers "як було", not "що робити". */}
          {history ? (
            <>
              <MonthBars data={data} selected={selected ?? data.selectedMonth} onSelect={onSelectMonth} />
              <Saved data={data} />
              <Categories data={data} />
            </>
          ) : (
            <button
              onClick={() => setHistory(true)}
              className="w-full rounded-2xl border border-dashed border-neutral-300 dark:border-neutral-700 px-4 py-3 text-sm text-neutral-500"
            >
              Історія за пів року — доходи, витрати, заощадження
            </button>
          )}
        </>
      )}
    </Screen>
  )
}

/// The headline: how this week — or this month — is going, against the same stretch one step
/// back. It leads because it is the only thing on the screen that can still be acted on today,
/// and because a total on its own answers nothing: "їжа 380 zł" means nothing without
/// "минулого тижня 240".
///
/// Both windows are the CALENDAR's. They used to be "останні 7 днів" and "останні 14", which
/// re-base themselves every morning — so no two readings are of the same thing, and "минулого
/// тижня" meant something nobody else in the world means by it. The comparison stops at the
/// matching day of the earlier stretch, because a Wednesday against a whole week is a fall in
/// spending that never happened.
///
/// The categories underneath rank by MONEY, not by how often. The home screen's shortcut row
/// already ranks by frequency, and that answers a different question — thirty coffees and one
/// taxi look nothing alike in a list of counts and identical in a wallet.
function RecentCard({ recent, window, onWindow }: {
  recent: RecentSpending | null
  window: SpendWindow
  onWindow: (w: SpendWindow) => void
}) {
  const [detail, setDetail] = useState(false)
  if (!recent) return <CardSkeleton />

  const c = recent.currency
  const diff = recent.total - recent.previousTotal
  const comparable = recent.previousTotal > 0
  const share = comparable ? Math.round((diff / recent.previousTotal) * 100) : 0

  return (
    <Card>
      <div className="flex items-baseline justify-between gap-3">
        <SectionTitle>{window === 'week' ? 'Цей тиждень' : 'Цей місяць'}</SectionTitle>
        <div className="flex gap-1">
          {([['week', 'Тиждень'], ['month', 'Місяць']] as const).map(([key, label]) => (
            <button
              key={key}
              onClick={() => onWindow(key)}
              aria-pressed={window === key}
              className={`rounded-lg px-2 py-1 text-xs ${
                window === key
                  ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 font-medium'
                  : 'text-neutral-400'
              }`}
            >
              {label}
            </button>
          ))}
        </div>
      </div>

      <p className="text-3xl font-bold tabular-nums">{money(recent.total, c)}</p>

      {/* One sentence, and it is the point of the card. A number with a percentage bolted onto
          it still has to be assembled by the reader; a sentence has already been read. */}
      <p className="text-sm text-neutral-500">
        {recent.total === 0
          ? 'Витрат ще не було.'
          : !comparable
            ? `За ${recent.days} ${plural(recent.days, 'день', 'дні', 'днів')} — порівняти поки нема з чим.`
            : (
              <>
                Це на{' '}
                <span className={diff > 0 ? 'font-medium text-amber-600' : 'font-medium text-emerald-600'}>
                  {money(Math.abs(diff), c)}
                  {Math.abs(share) > 0 && ` (${Math.abs(share)}%)`}
                </span>{' '}
                {diff > 0 ? 'більше' : 'менше'}, ніж{' '}
                {window === 'week' ? 'за ті самі дні минулого тижня' : 'за ті самі дні минулого місяця'}
                {' '}— {money(recent.previousTotal, c)}.
              </>
            )}
      </p>

      {recent.categories.length > 0 && (
        <>
          <div className="space-y-1.5">
            {/* Three lines by default. A list of everything that moved is a wall of numbers,
                and by the fourth line nobody is deciding anything — but the rest is one tap
                away, because "куди ж воно все пішло" is a real question. */}
            {(detail ? recent.categories : recent.categories.slice(0, 3)).map((r) => {
              const moved = r.amount - r.previousAmount
              return (
                <div key={r.categoryId} className="flex items-baseline justify-between gap-3 text-sm">
                  <span className="truncate">{r.icon ? `${r.icon} ` : ''}{r.name}</span>
                  <span className="tabular-nums text-right shrink-0">
                    {money(r.amount, c)}
                    <span className="block text-xs text-neutral-400">
                      {r.count} × сер. {money(r.amount / r.count, c)}
                      {/* Silent when there is nothing to compare with: a category first used
                          this week is not "+100%", it is new. */}
                      {r.previousAmount > 0 && (
                        <span className={moved > 0 ? ' text-amber-600' : ' text-emerald-600'}>
                          {' · '}{moved > 0 ? '+' : '−'}{money(Math.abs(moved), c)}
                        </span>
                      )}
                    </span>
                  </span>
                </div>
              )
            })}
          </div>

          {recent.categories.length > 3 && (
            <button
              onClick={() => setDetail(!detail)}
              className="text-xs text-neutral-400 underline"
            >
              {detail ? 'Згорнути' : `Усі категорії — ще ${recent.categories.length - 3}`}
            </button>
          )}
        </>
      )}

      {/* Says which days were compared, so a figure that looks wrong can be checked instead of
          argued with. */}
      {comparable && (
        <p className="text-xs text-neutral-400">
          {dayMonth(recent.from)} – {dayMonth(recent.to)} проти{' '}
          {dayMonth(recent.previousFrom)} – {dayMonth(recent.previousTo)}
        </p>
      )}
    </Card>
  )
}

/// A category is only worth mentioning when it is over its own normal by a margin that is not
/// noise. A tenth is the line: below it the difference is one extra trip to the shop, and a
/// screen that flags those is a screen that gets ignored.
const NOTABLE = 0.1

/// How much more this month cost than the category usually does. Null — no history to compare
/// against, which is not the same as "no difference" and must never render as a zero.
function overrun(c: CategoryStats): number | null {
  return c.typical === null ? null : c.amount - c.typical
}

/// The answer to "куди більше йде", which is never the largest category — rent is the largest
/// category every month and there is nothing to do about it. It is the category that is larger
/// than ITSELF, and that is the only thing on this screen that can be acted on today.
///
/// Three at most: a list of everything that moved is the same wall of numbers the rest of the
/// screen already is, and by the fourth line nobody is deciding anything.
function Overruns({ data }: { data: StatsData }) {
  const compared = data.categories.filter((c) => c.typical !== null)
  if (compared.length === 0) return null

  const over = compared
    .filter((c) => overrun(c)! / c.typical! >= NOTABLE)
    .sort((a, b) => overrun(b)! - overrun(a)!)
    .slice(0, 3)

  return (
    <Card>
      <SectionTitle>Що вилізло за межу — {monthLabel(data.selectedMonth)}</SectionTitle>

      {over.length === 0 ? (
        <p className="text-sm text-neutral-500">
          Нічого незвичного: усі категорії в межах того, що ти витрачаєш зазвичай.
        </p>
      ) : (
        <div className="space-y-2">
          {over.map((c) => (
            <div key={c.categoryId} className="flex items-baseline justify-between gap-3 text-sm">
              <span>{c.icon ? `${c.icon} ` : ''}{c.name}</span>
              <span className="tabular-nums text-right">
                <span className="font-medium text-amber-600">
                  +{money(overrun(c)!, data.currency)}
                </span>
                <span className="block text-xs text-neutral-400">
                  {money(c.amount, data.currency)} проти звичних {money(c.typical!, data.currency)}
                </span>
              </span>
            </div>
          ))}
        </div>
      )}

      <p className="text-xs text-neutral-400">
        «Звичне» — медіана цієї ж категорії за три попередні місяці. Медіана, а не середнє: один
        дорогий місяць не має ставати новою нормою.
      </p>
    </Card>
  )
}

/// The other half of "що можна оптимізувати", and the half that needs no willpower: a standing
/// charge is cancelled once and saves every month after, where eating out less has to be
/// decided again every week.
///
/// The dearest single row is named because that is where the decision actually is — a total
/// says there is a problem, one name says what to do about it.
function Subscriptions({ items }: { items: Recurring[] }) {
  const live = items.filter((r) => r.active && r.kind !== 'Income')
  if (live.length === 0) return null

  const totals = monthlyTotals(live)
  const dearest = live.reduce((a, b) =>
    perMonth(b.amountOriginal, b.unit, b.interval) > perMonth(a.amountOriginal, a.unit, a.interval) ? b : a)

  return (
    <Card>
      <div className="flex items-baseline justify-between gap-3">
        <SectionTitle>Регулярні платежі</SectionTitle>
        <span className="text-sm font-medium tabular-nums">
          {totals.map((t) => money(t.expense, t.currency)).join(' · ')}
        </span>
      </div>

      <p className="text-sm text-neutral-500">
        {live.length === 1 ? 'Один платіж' : `${live.length} платежів`} на місяць. Найдорожчий —{' '}
        {dearest.categoryName}
        {dearest.note ? ` (${dearest.note})` : ''}:{' '}
        {money(perMonth(dearest.amountOriginal, dearest.unit, dearest.interval), dearest.currencyOriginal)} на
        місяць.
      </p>

      <p className="text-xs text-neutral-400">
        Річні й тижневі рахунки приведено до місяця, щоб їх можна було порівняти між собою.
        Призупинені не рахуються.
      </p>
    </Card>
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

/// "Скільки я відкладаю" — and, right beside it, the answer to the question that always comes
/// next: how the allocation scheme and the jars relate to each other.
///
/// They are two halves of one number. The scheme moves its share into jars by itself at the
/// start of every period (that is "за схемою"); anything the user adds on top, takes back out,
/// or pays straight out of a jar is "руками". Summed, that is what actually stayed put — and
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
        <span className="text-sm font-medium tabular-nums">
          {money(data.savedBalance, c)}
        </span>
      </div>

      {/* Kept in the currency it was put in. A single converted figure hides both what is
          actually held and the fact that half of it moves with the rate — which is the whole
          point for someone living between currencies. Shown only when there IS more than one. */}
      {data.savedByCurrency && (
        <p className="text-sm tabular-nums">
          {data.savedByCurrency.map((x) => money(x.amount, x.currency)).join(' · ')}
          <span className="block text-xs text-neutral-400">
            У валютах, у яких відкладав — без перерахунку.
          </span>
        </p>
      )}

      <p className="text-sm text-neutral-500">
        {/* The months below are what MOVED — the balance above is what is there. The two
            differ by anything set aside before the app was told about it, and saying so is
            cheaper than letting the arithmetic look broken. */}
        Відкладено за {months.length} міс.: {money(total, c)}
        {income > 0 && ` · ${rate(total, income)} доходу`}
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
                {/* Both halves only when they differ — "500 за схемою" under a row that
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
                  <Delta category={c} />
                </span>
              </div>
              <div className="mt-1 h-2 rounded-full bg-neutral-100 dark:bg-neutral-800">
                <div className="h-2 rounded-full bg-neutral-900 dark:bg-white" style={{ width: `${c.percent}%` }} />
              </div>
              {/* Two ways to spend the same money, undone in opposite directions: thirty
                  small buys is a habit, three big ones is a decision. The bar above cannot
                  tell them apart, so the line under it does. */}
              <p className="mt-1 text-xs text-neutral-400">
                {c.count} × сер. {money(c.amount / c.count, data.currency)}
              </p>
            </div>
          ))}
        </div>
      )}
    </Card>
  )
}

/// The category's own month against its own normal, as a percent. Shown only past the same
/// threshold the overrun card uses, in both directions — a category that came in under its
/// normal is worth the same glance, and it is the only praise this screen has to give.
/// Silent when there is no history: an absent comparison is honest, a "0%" is a lie.
function Delta({ category }: { category: CategoryStats }) {
  const diff = overrun(category)
  if (diff === null || Math.abs(diff) / category.typical! < NOTABLE) return null

  const share = Math.round((diff / category.typical!) * 100)
  return (
    <span className={diff > 0 ? ' text-amber-600' : ' text-emerald-600'}>
      {' '}{diff > 0 ? '+' : ''}{share}%
    </span>
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
