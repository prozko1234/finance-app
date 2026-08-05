import type { CarryoverDecision, EnvelopeSummary, FrequentCategory, SafeToSpend, Transaction } from '../types'
import { dayHeading, dayMonth, money, signedMoney, signedMoneyClass } from '../format'
import { envelopeIcon } from '../envelopeWords'

interface Props {
  summary: SafeToSpend | null
  transactions: Transaction[]
  /// The payday question for someone who never saw it: onboarding only shows on an empty app,
  /// so anyone who already had data is still on a period that starts on the 1st.
  /// Null — there is nothing to ask.
  paydayNudge: { onGo: () => void; onDismiss: () => void } | null
  /// There is more to show — the server returned exactly as many rows as were asked for.
  canLoadMore: boolean
  onLoadMore: () => void
  onDelete: (id: number) => void
  onAddIncome: () => void
  /// The shortcut row, already ranked and windowed by the server.
  frequent: FrequentCategory[]
  onQuickCategory: (categoryId: number) => void
  onEdit: (t: Transaction) => void
  onGoSavings: () => void
  onGoAllocation: () => void
  onGoBalance: () => void
  /// Where last period's leftover goes. Asked only when there is one and nobody has placed it.
  onDecideCarryover: (decision: CarryoverDecision) => void
}

export function Home({
  summary, transactions, paydayNudge, canLoadMore, onLoadMore, onDelete, onAddIncome, frequent, onQuickCategory, onEdit, onGoSavings, onGoAllocation,
  onGoBalance, onDecideCarryover,
}: Props) {
  return (
    <div className="space-y-6">
      <SafeToSpendCard summary={summary} onAddIncome={onAddIncome} />
      {summary?.carryover && (
        <CarryoverCard
          carryover={summary.carryover}
          currency={summary.currency}
          onDecide={onDecideCarryover}
        />
      )}
      {paydayNudge && <PaydayNudge {...paydayNudge} />}
      {summary?.budgetSet && (
        <PeriodCard summary={summary} onGoAllocation={onGoAllocation} onGoBalance={onGoBalance} />
      )}
      {summary && (
        <EnvelopesCard envelopes={summary.envelopes} currency={summary.currency} onOpen={onGoSavings} />
      )}
      {frequent.length > 0 && <QuickRow categories={frequent} onPick={onQuickCategory} />}
      <RecentList
        transactions={transactions}
        canLoadMore={canLoadMore}
        onLoadMore={onLoadMore}
        onDelete={onDelete}
        onEdit={onEdit}
      />
    </div>
  )
}

/// The money that survived the last period, and the one question it raises. Until this card
/// existed the leftover simply vanished: a new period's budget is the new income, so anything
/// underspent lived only in the bank account — and the app, which asks to be trusted with one
/// number, was quietly poorer than reality every month.
///
/// Asked rather than moved automatically, because a leftover is sometimes last month's win and
/// sometimes the money for a thing planned for next week, and no rule can tell those apart. The
/// answer that is usually right is the wide button; the other two are one tap away and not one
/// of them is destructive. Ignoring it is an answer too — that is what stops it coming back.
function CarryoverCard({ carryover, currency, onDecide }: {
  carryover: NonNullable<SafeToSpend['carryover']>
  currency: string
  onDecide: (decision: CarryoverDecision) => void
}) {
  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-2">
      <p className="text-sm font-medium">
        Минулого періоду лишилось {money(carryover.amount, currency)}
      </p>
      <p className="text-xs text-neutral-500">
        За {dayMonth(carryover.fromStart)} – {dayMonth(carryover.fromEnd)} ти не витратив цих
        грошей. Вони на рахунку — скажи, чим їх вважати, і застосунок перестане про це питати.
      </p>
      <button
        onClick={() => onDecide('ToEnvelope')}
        className="w-full rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-3 py-2 text-sm font-medium"
      >
        У банку «{carryover.envelopeName}»
      </button>
      <div className="flex gap-2">
        <button
          onClick={() => onDecide('ToBudget')}
          className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm"
        >
          Лишити на витрати
        </button>
        <button
          onClick={() => onDecide('Ignore')}
          className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm text-neutral-500"
        >
          Не рахувати
        </button>
      </div>
    </div>
  )
}

/// One question, once. Asked here because this is the figure it changes: while the period
/// starts on the 1st, the norm at the end of the month promises money the account no longer
/// has.
///
/// "Гроші приходять 1-го" closes it for good — a nudge that comes back is no longer a
/// question, it is a reproach.
function PaydayNudge({ onGo, onDismiss }: { onGo: () => void; onDismiss: () => void }) {
  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-2">
      <p className="text-sm font-medium">Коли до тебе приходять гроші?</p>
      <p className="text-xs text-neutral-500">
        Зараз період рахується з 1 числа. Якщо зарплата приходить, скажімо, 10-го — цифра
        наприкінці місяця обіцяє те, чого на рахунку вже немає.
      </p>
      <div className="flex gap-2">
        <button
          onClick={onGo}
          className="flex-1 rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-3 py-2 text-sm font-medium"
        >
          Вказати день
        </button>
        <button
          onClick={onDismiss}
          className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm text-neutral-500"
        >
          Гроші приходять 1-го
        </button>
      </div>
    </div>
  )
}

/// A tap opens the form with the category already chosen — only the amount is left.
/// The amount is deliberately not guessed: the category repeats, the exact sum does not.
///
/// The heading names the window the server counted over. "Часті категорії" on its own is a
/// claim with no period behind it — the user cannot tell whether it means this week or the
/// whole history, and so cannot tell whether the row is wrong.
function QuickRow({ categories, onPick }: {
  categories: FrequentCategory[]; onPick: (categoryId: number) => void
}) {
  return (
    <div>
      <h2 className="text-sm font-medium text-neutral-400 mb-2 px-1">
        Часто за {categories[0].days} днів
      </h2>
      <div className="flex gap-2 flex-wrap">
        {categories.map((c) => (
          <button
            key={c.categoryId}
            onClick={() => onPick(c.categoryId)}
            className="flex-1 min-w-[30%] rounded-xl bg-white dark:bg-neutral-900 px-3 py-3 shadow-sm text-left"
          >
            <span className="text-lg">{c.icon}</span>
            <p className="text-sm font-medium truncate">{c.name}</p>
          </button>
        ))}
      </div>
    </div>
  )
}

function SafeToSpendCard({ summary, onAddIncome }: { summary: SafeToSpend | null; onAddIncome: () => void }) {
  if (!summary) {
    return <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm animate-pulse h-40" />
  }

  // The same question on the first run and at the start of every period: how much arrived.
  // This used to offer "задати місячний бюджет" — a made-up figure that then lived its own
  // life beside the real income.
  if (!summary.budgetSet) {
    return (
      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center space-y-3">
        <p className="font-medium">Новий період — скільки прийшло?</p>
        <p className="text-sm text-neutral-500">
          Період почався {dayMonth(summary.periodStart)}, доходу за нього ще немає.
        </p>
        <button
          onClick={onAddIncome}
          className="rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-4 py-2 font-medium"
        >
          Вписати дохід
        </button>
      </div>
    )
  }

  const left = summary.leftToday ?? 0
  const positive = left >= 0
  const c = summary.currency

  // One figure, one line under it. Everything explaining where it came from lives in the
  // period card below: until M25 there was a paragraph about the counting window here too, and
  // the home screen read as prose rather than as the answer to one question.
  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center">
      <p className="text-sm uppercase tracking-wide text-neutral-400">
        {positive ? 'Можна витратити сьогодні' : 'Понад норму сьогодні'}
      </p>
      <p className={`mt-1 text-5xl font-bold tabular-nums ${positive ? 'text-emerald-600' : 'text-red-600'}`}>
        {money(positive ? left : -left, c)}
      </p>
      <p className="mt-2 text-sm text-neutral-500">
        {summary.spentToday > 0
          ? `Норма ${money(summary.dailyNorm ?? 0, c)}, витрачено ${money(summary.spentToday, c)}`
          : `Норма на день · ще ${summary.daysLeftInPeriod} дн.`}
      </p>
      <TomorrowNote summary={summary} />
    </div>
  )
}

/// "Місяць" while the money arrives on the 1st. When payday is another day, month is the wrong
/// word: a period of 10.07–09.08 labelled as a month reads as a bug, so it shows the dates
/// themselves instead.
function periodLabel(summary: SafeToSpend): string {
  if (!summary.periodStart || !summary.periodEnd) return 'Місяць'
  if (Number(summary.periodStart.slice(8, 10)) === 1) return 'Місяць'

  return `${dayMonth(summary.periodStart)} – ${dayMonth(summary.periodEnd)}`
}

/// The point of M15: today's spending already changes tomorrow's number — say it out loud
/// instead of letting the figure quietly slide. Statement of fact, never a scolding.
function TomorrowNote({ summary }: { summary: SafeToSpend }) {
  const { tomorrowIfStop, tomorrowIfOnPlan, currency: c } = summary
  if (tomorrowIfStop === null || tomorrowIfOnPlan === null || summary.spentToday === 0) return null

  const diff = tomorrowIfStop - tomorrowIfOnPlan
  if (Math.abs(diff) < 0.01) return null

  return (
    <p className={`mt-2 text-xs ${diff < 0 ? 'text-amber-600' : 'text-emerald-600'}`}>
      Завтра {money(tomorrowIfStop, c)} замість {money(tomorrowIfOnPlan, c)}
    </p>
  )
}

/// The period as three figures on one line, not a column of seven. Until M25 this was a table
/// with taxes, the allocation scheme and two disclosures: finding out "скільки лишилось" meant
/// reading the whole month. The card now answers in one line, and what is needed once a month
/// lives on its own screen — taxes on the tax screen, buckets on the allocation one — where it
/// is there in full anyway.
function PeriodCard({ summary, onGoAllocation, onGoBalance }: {
  summary: SafeToSpend; onGoAllocation: () => void; onGoBalance: () => void
}) {
  const c = summary.currency
  const taxes = summary.monthTaxes
  const held = heldBack(summary)
  const split = summary.allocation !== null && summary.allocation.buckets.length > 1
  const from = summary.fromOpeningBalance && summary.windowStart
    ? dayMonth(summary.windowStart)
    : null

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-1.5">
      <div className="flex items-baseline justify-between gap-3">
        {/* When the budget comes from a counted balance, the heading leads to where that
            figure can be recounted or cleared. Otherwise it is just the period's boundaries,
            with nothing to press. */}
        {from ? (
          <button onClick={onGoBalance} className="text-sm font-medium text-neutral-400">
            З {from} · залишок
          </button>
        ) : (
          <h2 className="text-sm font-medium text-neutral-400">{periodLabel(summary)}</h2>
        )}
        <button onClick={onGoAllocation} className="text-xs text-neutral-400 shrink-0">
          {split ? `${summary.allocation!.schemeName} →` : 'Розподіл →'}
        </button>
      </div>

      <p className="text-sm tabular-nums">
        <span className="text-neutral-400">{from ? 'Було' : 'Бюджет'} </span>
        {money(summary.periodBudget ?? 0, c)}
        <span className="text-neutral-400"> · витрачено </span>
        {money(summary.spentThisPeriod, c)}
        <span className="text-neutral-400"> · лишилось </span>
        <span className="font-semibold">{money(summary.remainingThisPeriod ?? 0, c)}</span>
      </p>

      {(held > 0 || summary.reservedRecurring > 0) && (
        <p className="text-xs text-neutral-400 tabular-nums">
          {[
            held > 0 ? `у банках ${money(held, c)}` : null,
            summary.reservedRecurring > 0 ? `на підписки ${money(summary.reservedRecurring, c)}` : null,
          ].filter(Boolean).join(' · ')}
          {' — уже відкладено з бюджету'}
        </p>
      )}

      {/* Taxes stay in the engine's currency: these are the bookkeeper's figures, and a sum
          labelled in hryvnia would match no document at all. The VAT/ZUS split lives on the
          tax screen. */}
      {taxes && (
        <p className="text-xs text-neutral-400 tabular-nums">
          Прийшло {money(taxes.gross, taxes.currency)} · на податки{' '}
          {money(taxes.setAside, taxes.currency)}
        </p>
      )}
    </div>
  )
}

/// How much the month's arithmetic is holding in jars. One line rather than one per bucket:
/// what has already been put aside by hand and what is still held back from the budget are the
/// same reserved money, and two lines would read as holding it twice.
function heldBack(summary: SafeToSpend): number {
  return summary.envelopes.reduce((s, e) => s + e.depositedThisMonth + e.stillToReserve, 0)
}

/// Jars get their own card, not lines in the period summary: a balance that survives
/// across months is a different kind of number from this month's arithmetic.
///
/// Every pot is listed, not just savings. A scheme with a pension bucket used to hold money
/// back every month and never show where it went — the whole point of the card is that the
/// pile is visible.
function EnvelopesCard({ envelopes, currency, onOpen }: {
  envelopes: EnvelopeSummary[]; currency: string; onOpen: () => void
}) {
  const alive = envelopes.filter((e) => e.balance !== 0 || e.monthGoal > 0)

  if (alive.length === 0) {
    return (
      <button
        onClick={onOpen}
        className="w-full rounded-2xl border border-dashed border-neutral-300 dark:border-neutral-700 p-4 text-sm text-neutral-500"
      >
        + Відкладати щомісяця — на подушку, пенсію чи бажання
      </button>
    )
  }

  const total = alive.reduce((s, e) => s + e.balance, 0)

  return (
    <button onClick={onOpen} className="w-full rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm text-left">
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-sm text-neutral-400">Відкладено</span>
        <span className="text-2xl font-bold tabular-nums">{money(total, currency)}</span>
      </div>

      <dl className="mt-3 space-y-1.5 text-sm">
        {alive.map((e) => (
          <div key={e.id} className="flex justify-between gap-3">
            <dt className="truncate">
              {envelopeIcon(e.kind)} {e.name}
              {e.monthGoal > 0 && (
                <span className="text-neutral-400 text-xs">
                  {' · '}
                  {/* The scheme fills the jar itself, so the plan is usually met. The amount
                      is not repeated here: it already stands on the right of the same row, and
                      two identical numbers side by side did not fit on a phone. */}
                  {e.depositedThisMonth >= e.monthGoal
                    ? 'за планом ✓'
                    : `${money(e.depositedThisMonth, currency)} з ${money(e.monthGoal, currency)}`}
                </span>
              )}
            </dt>
            <dd className="tabular-nums shrink-0">{money(e.balance, currency)}</dd>
          </div>
        ))}
      </dl>
    </button>
  )
}

function RecentList({ transactions, canLoadMore, onLoadMore, onDelete, onEdit }: {
  transactions: Transaction[]
  canLoadMore: boolean
  onLoadMore: () => void
  onDelete: (id: number) => void
  onEdit: (t: Transaction) => void
}) {
  if (transactions.length === 0) {
    return <p className="text-center text-neutral-400 text-sm">Ще немає транзакцій. Додай першу кнопкою +</p>
  }

  return (
    <div className="space-y-4">
      <h2 className="text-sm font-medium text-neutral-400 px-1">Останні</h2>
      {groupByDay(transactions).map(([date, rows]) => (
      <div key={date} className="space-y-2">
      <p className="text-xs text-neutral-400 px-1">{dayHeading(date)}</p>
      <ul className="space-y-2">
        {rows.map((t) => (
          <li
            key={t.id}
            className="flex items-center gap-3 rounded-xl bg-white dark:bg-neutral-900 px-4 py-3 shadow-sm"
          >
            <span className="text-xl">{t.kind === 'Income' ? '💰' : iconFor(t)}</span>
            <button
              // Income opens too, in the income form, because it still carries VAT. Tapping an
              // income row used to do nothing, so correcting an invoice meant deleting and
              // retyping it — which is exactly where a figure gets lost.
              onClick={() => onEdit(t)}
              className="flex-1 min-w-0 text-left"
            >
              <p className="font-medium truncate">
                {t.kind === 'Income' ? 'Дохід' : t.categoryName}
                {/* Says at a glance why this expense did not reduce the daily norm. */}
                {t.envelopeName && (
                  <span className="text-xs text-neutral-400"> · з «{t.envelopeName}»</span>
                )}
              </p>
              <p className="text-xs text-neutral-400 truncate">
                {t.note || t.merchant || (t.kind === 'Income' ? 'дохід' : '')}
              </p>
            </button>
            <div className="text-right">
              <p className={`font-semibold tabular-nums ${signedMoneyClass(t.kind)}`}>
                {signedMoney(t.amountOriginal, t.currencyOriginal, t.kind)}
              </p>
              {t.currencyOriginal !== t.displayCurrency && (
                <p className="text-xs text-neutral-400 tabular-nums">≈ {money(t.amountDisplay, t.displayCurrency)}</p>
              )}
            </div>
            <button
              onClick={() => onDelete(t.id)}
              className="text-neutral-300 hover:text-red-500 px-1"
              aria-label="Видалити"
            >
              ✕
            </button>
          </li>
        ))}
      </ul>
      </div>
      ))}
      {/* Load more instead of a hard ceiling: nobody needs a list longer than the screen
          every day, but "where was that expense last week" does happen. */}
      {canLoadMore && (
        <button
          onClick={onLoadMore}
          className="w-full rounded-xl border border-dashed border-neutral-300 dark:border-neutral-700 px-4 py-2.5 text-sm text-neutral-500"
        >
          Показати ще
        </button>
      )}
    </div>
  )
}

/// Days come in the order the rows arrived — the server already returns them newest first.
function groupByDay(rows: Transaction[]): [string, Transaction[]][] {
  const days = new Map<string, Transaction[]>()
  for (const t of rows) {
    const day = days.get(t.date)
    if (day) day.push(t)
    else days.set(t.date, [t])
  }
  return [...days.entries()]
}

/// The emoji comes from the transaction itself — the one set on its category. There used to be
/// a name → emoji table here covering the six starter categories, so every other category,
/// whether made by hand or renamed, showed the same 📦 while its own emoji sat unused in
/// settings. 📦 now only stands in for a category that was never given one.
function iconFor(t: Transaction): string {
  return t.categoryIcon || '📦'
}
