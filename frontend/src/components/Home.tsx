import type { ReactNode } from 'react'
import type { EnvelopeSummary, SafeToSpend, Transaction } from '../types'
import { dayMonth, money } from '../format'
import { buildQuickCategories, type QuickCategory } from '../quickCategories'
import { RatesNote } from './Screen'

interface Props {
  summary: SafeToSpend | null
  transactions: Transaction[]
  onDelete: (id: number) => void
  onGoSettings: () => void
  onQuickCategory: (categoryId: number) => void
  onEdit: (t: Transaction) => void
  onGoSavings: () => void
  onGoAllocation: () => void
}

export function Home({
  summary, transactions, onDelete, onGoSettings, onQuickCategory, onEdit, onGoSavings, onGoAllocation,
}: Props) {
  const quick = buildQuickCategories(transactions, (name) => ICONS[name] ?? '📦')

  return (
    <div className="space-y-6">
      <SafeToSpendCard summary={summary} onGoSettings={onGoSettings} />
      {summary?.budgetSet && <MonthCard summary={summary} onGoAllocation={onGoAllocation} />}
      {summary && (
        <EnvelopesCard envelopes={summary.envelopes} currency={summary.currency} onOpen={onGoSavings} />
      )}
      {quick.length > 0 && <QuickRow categories={quick} onPick={onQuickCategory} />}
      <RecentList transactions={transactions} onDelete={onDelete} onEdit={onEdit} />
    </div>
  )
}

/// A tap opens the form with the category already chosen — only the amount is left.
/// The amount is deliberately not guessed: the category repeats, the exact sum does not.
function QuickRow({ categories, onPick }: {
  categories: QuickCategory[]; onPick: (categoryId: number) => void
}) {
  return (
    <div>
      <h2 className="text-sm font-medium text-neutral-400 mb-2 px-1">Часті категорії</h2>
      <div className="flex gap-2 flex-wrap">
        {categories.map((c) => (
          <button
            key={c.categoryId}
            onClick={() => onPick(c.categoryId)}
            className="flex-1 min-w-[30%] rounded-xl bg-white dark:bg-neutral-900 px-3 py-3 shadow-sm text-left"
          >
            <span className="text-lg">{c.icon}</span>
            <p className="text-sm font-medium truncate">{c.categoryName}</p>
          </button>
        ))}
      </div>
    </div>
  )
}

function SafeToSpendCard({ summary, onGoSettings }: { summary: SafeToSpend | null; onGoSettings: () => void }) {
  if (!summary) {
    return <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm animate-pulse h-40" />
  }

  if (!summary.budgetSet) {
    return (
      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center space-y-3">
        <p className="font-medium">Ще нема з чого рахувати</p>
        <p className="text-sm text-neutral-500 leading-relaxed">
          Додатку потрібно знати, скільки грошей у тебе на цей місяць. Тоді він щодня
          казатиме одну цифру: скільки можна витратити сьогодні.
        </p>
        <button
          onClick={onGoSettings}
          className="rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-4 py-2 font-medium"
        >
          Задати місячний бюджет
        </button>
      </div>
    )
  }

  const left = summary.leftToday ?? 0
  const positive = left >= 0
  const c = summary.currency

  // Одне речення замість чотирьох рядків дрібних чисел. Залишок місяця, бюджет і
  // витрачене живуть у картці «Місяць» — до M24 вони були в обох, і два місця з тією
  // самою арифметикою читались як дві різні цифри.
  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center">
      <p className="text-sm uppercase tracking-wide text-neutral-400">
        {positive ? 'Можна витратити сьогодні' : 'Понад норму сьогодні'}
      </p>
      <p className={`mt-1 text-5xl font-bold tabular-nums ${positive ? 'text-emerald-600' : 'text-red-600'}`}>
        {money(positive ? left : -left, c)}
      </p>
      <p className="mt-2 text-sm text-neutral-500 leading-relaxed">
        {summary.spentToday > 0
          ? `З норми ${money(summary.dailyNorm ?? 0, c)} на сьогодні вже витрачено ${money(summary.spentToday, c)}.`
          : `Це норма на сьогодні. Лишилось ${summary.daysLeftInPeriod} дн.`}
      </p>
      <TomorrowNote summary={summary} />
      <WindowNote summary={summary} />
    </div>
  )
}

/// «Місяць», поки гроші приходять 1 числа. Коли зарплата в інший день, місяць — не те
/// слово: період 10.07–09.08 названий місяцем читається як помилка додатка, тому там
/// стоять самі дати.
function periodLabel(summary: SafeToSpend): string {
  if (!summary.periodStart || !summary.periodEnd) return 'Місяць'
  if (Number(summary.periodStart.slice(8, 10)) === 1) return 'Місяць'

  return `${dayMonth(summary.periodStart)} – ${dayMonth(summary.periodEnd)}`
}

/// Коли рахунок іде не з 1 числа, це треба сказати прямо — інакше «витрачено» виглядає
/// підозріло малим, і незрозуміло, чи додаток щось загубив.
function WindowNote({ summary }: { summary: SafeToSpend }) {
  if (!summary.fromOpeningBalance || !summary.windowStart) return null

  return (
    <p className="mt-3 text-xs text-neutral-400 leading-relaxed">
      Рахуємо з {dayMonth(summary.windowStart)} — від суми, яку ти тоді порахував.
      Те, що витрачено раніше, вже в ній.
    </p>
  )
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

/// Одна картка на весь місяць: звідки взявся бюджет, що з нього вже пішло і що лишилось.
/// До M23 це були три окремі картки (підсумок, розподіл, податки) — три заголовки для
/// однієї арифметики. Тепер один стовпчик, який справді сходиться, а деталі — під
/// розкривачками, щоб головна не була простинею.
function MonthCard({ summary, onGoAllocation }: { summary: SafeToSpend; onGoAllocation: () => void }) {
  const c = summary.currency
  const taxes = summary.monthTaxes
  const a = summary.allocation
  const split = a !== null && a.buckets.length > 1
  const from = summary.fromOpeningBalance && summary.windowStart
    ? dayMonth(summary.windowStart)
    : null

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm">
      <h2 className="text-sm font-medium text-neutral-400 mb-3">
        {from ? `З ${from}` : periodLabel(summary)}
      </h2>
      <dl className="space-y-1.5 text-sm">
        {taxes && (
          <>
            {/* Податкові рядки лишаються у валюті рушія — це цифри для книгової. Показати
                їх із міткою гривні означало б написати суму, якої немає в жодному документі. */}
            <Row label="Прийшло на рахунок" value={money(taxes.gross, taxes.currency)} />
            <Row label="Відкладено на податки" value={`− ${money(taxes.setAside, taxes.currency)}`} muted />
            <Details summary="з чого · VAT, ZUS, здоровотна, податок">
              <Row label="VAT" value={money(taxes.vat, taxes.currency)} small />
              <Row label="ZUS, соціальні внески" value={money(taxes.zusSocial, taxes.currency)} small />
              <Row label="Здоровотна" value={money(taxes.health, taxes.currency)} small />
              <Row label="Податок (ryczałt)" value={money(taxes.tax, taxes.currency)} small />
              <RatesNote year={taxes.ratesYear} />
            </Details>
            {taxes.currency !== c && (
              <p className="text-xs text-neutral-400 leading-relaxed pt-0.5">
                Податки рахуються у {taxes.currency} — так їх бачить книгова. Нижче все
                у {c}.
              </p>
            )}
          </>
        )}

        <Row
          label={from ? 'Було на руках' : 'Бюджет періоду'}
          value={money(summary.periodBudget ?? 0, c)}
          strong
        />
        <Row label={from ? `Витрачено з ${from}` : 'Витрачено'} value={`− ${money(summary.spentThisPeriod, c)}`} muted />

        {summary.reservedRecurring > 0 && (
          <Row label="Зарезервовано на підписки" value={`− ${money(summary.reservedRecurring, c)}`} muted />
        )}
        {heldBack(summary) > 0 && (
          <Row label="Відкладено у конверти" value={`− ${money(heldBack(summary), c)}`} muted />
        )}

        {split && (
          <Details summary={`куди пішов бюджет · ${a!.schemeName}`}>
            {a!.buckets.map((b) => (
              <Row key={b.id} label={`${b.name} · ${b.percent}%`} value={money(b.amount, c)} small />
            ))}
          </Details>
        )}

        <Row label="Лишилось" value={money(summary.remainingThisPeriod ?? 0, c)} strong />
      </dl>

      <button onClick={onGoAllocation} className="mt-3 text-xs text-neutral-400">
        {split ? 'Змінити розподіл →' : 'Ділити бюджет за схемою (50/30/20 та інші) →'}
      </button>
    </div>
  )
}

/// Розкривачка з однаковим виглядом у всій картці — щоб «деталі» скрізь означали одне.
function Details({ summary, children }: { summary: string; children: ReactNode }) {
  return (
    <details>
      <summary className="cursor-pointer list-none text-xs text-neutral-400 pl-3 py-1">{summary}</summary>
      <div className="pl-3 pt-1 space-y-1 text-xs text-neutral-400">{children}</div>
    </details>
  )
}

/// Скільки місячна арифметика тримає в конвертах. Одним рядком, а не по кошиках: вже
/// відкладене вручну і те, що ще тримається з бюджету, — це та сама зарезервована сума,
/// і два рядки читались би як подвійне утримання.
function heldBack(summary: SafeToSpend): number {
  return summary.envelopes.reduce((s, e) => s + e.depositedThisMonth + e.stillToReserve, 0)
}

/// Envelopes get their own card, not lines in the month summary: a balance that survives
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
              {ENVELOPE_ICONS[e.kind]} {e.name}
              {e.monthGoal > 0 && (
                <span className="text-neutral-400 text-xs">
                  {' · '}
                  {/* Схема відкладає сама, тож зазвичай тут «відкладено ✓». Розбіжність
                      лишається видимою: коли вручну довнесли понад план. */}
                  {e.depositedThisMonth >= e.monthGoal
                    ? `відкладено ${money(e.depositedThisMonth, currency)} ✓`
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

const ENVELOPE_ICONS: Record<string, string> = {
  Savings: '🐖', Investing: '📈', Debt: '🏦', Other: '📦', Spending: '💳',
}

function Row({ label, value, muted, strong, small }: {
  label: string; value: string; muted?: boolean; strong?: boolean; small?: boolean
}) {
  return (
    <div className={`flex justify-between gap-3 ${strong ? 'border-t border-neutral-100 dark:border-neutral-800 pt-1.5 font-semibold' : ''}`}>
      <dt className={muted || small ? 'text-neutral-400' : ''}>{label}</dt>
      <dd className={`tabular-nums shrink-0 ${muted ? 'text-neutral-400' : ''}`}>{value}</dd>
    </div>
  )
}

function RecentList({ transactions, onDelete, onEdit }: { transactions: Transaction[]; onDelete: (id: number) => void; onEdit: (t: Transaction) => void }) {
  if (transactions.length === 0) {
    return <p className="text-center text-neutral-400 text-sm">Ще немає транзакцій. Додай першу кнопкою +</p>
  }

  return (
    <div>
      <h2 className="text-sm font-medium text-neutral-400 mb-2 px-1">Останні</h2>
      <ul className="space-y-2">
        {transactions.map((t) => (
          <li
            key={t.id}
            className="flex items-center gap-3 rounded-xl bg-white dark:bg-neutral-900 px-4 py-3 shadow-sm"
          >
            <span className="text-xl">{t.kind === 'Income' ? '💰' : iconFor(t)}</span>
            <button
              onClick={() => t.kind === 'Expense' && onEdit(t)}
              disabled={t.kind !== 'Expense'}
              className="flex-1 min-w-0 text-left disabled:cursor-default"
            >
              <p className="font-medium truncate">
                {t.kind === 'Income' ? 'Дохід' : t.categoryName}
                {/* Видно одразу, чому ця витрата не зменшила денну норму. */}
                {t.envelopeName && (
                  <span className="text-xs text-neutral-400"> · з «{t.envelopeName}»</span>
                )}
              </p>
              <p className="text-xs text-neutral-400 truncate">
                {t.note || t.merchant || t.date}
              </p>
            </button>
            <div className="text-right">
              <p className={`font-semibold tabular-nums ${t.kind === 'Income' ? 'text-emerald-600' : ''}`}>
                {t.kind === 'Income' ? '+' : ''}{money(t.amountOriginal, t.currencyOriginal)}
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
  )
}

const ICONS: Record<string, string> = {
  Їжа: '🍽', Транспорт: '🚌', Житло: '🏠', "Здоров'я": '💊', Розваги: '🎮', Інше: '📦',
}

function iconFor(t: Transaction): string {
  return ICONS[t.categoryName] ?? '📦'
}
