import type { EnvelopeSummary, SafeToSpend, Transaction } from '../types'
import { dayMonth, money } from '../format'
import { envelopeIcon } from '../envelopeWords'
import { buildQuickCategories, type QuickCategory } from '../quickCategories'

interface Props {
  summary: SafeToSpend | null
  transactions: Transaction[]
  onDelete: (id: number) => void
  onAddIncome: () => void
  onQuickCategory: (categoryId: number) => void
  onEdit: (t: Transaction) => void
  onGoSavings: () => void
  onGoAllocation: () => void
  onGoBalance: () => void
}

export function Home({
  summary, transactions, onDelete, onAddIncome, onQuickCategory, onEdit, onGoSavings, onGoAllocation,
  onGoBalance,
}: Props) {
  const quick = buildQuickCategories(transactions, (name) => ICONS[name] ?? '📦')

  return (
    <div className="space-y-6">
      <SafeToSpendCard summary={summary} onAddIncome={onAddIncome} />
      {summary?.budgetSet && (
        <PeriodCard summary={summary} onGoAllocation={onGoAllocation} onGoBalance={onGoBalance} />
      )}
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

function SafeToSpendCard({ summary, onAddIncome }: { summary: SafeToSpend | null; onAddIncome: () => void }) {
  if (!summary) {
    return <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm animate-pulse h-40" />
  }

  // Питання одне й те саме і на першому запуску, і кожного разу, коли починається новий
  // період: скільки прийшло. Раніше тут пропонувалось «задати місячний бюджет» — вигадану
  // цифру, яка потім жила своїм життям поруч із реальним доходом.
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

  // Одна цифра, один рядок під нею. Все, що пояснює, звідки вона взялась, живе в картці
  // періоду нижче: до M25 тут був ще й абзац про вікно розрахунку, і головна читалась як
  // текст, а не як відповідь на одне питання.
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

/// «Місяць», поки гроші приходять 1 числа. Коли зарплата в інший день, місяць — не те
/// слово: період 10.07–09.08 названий місяцем читається як помилка додатка, тому там
/// стоять самі дати.
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

/// Період — три цифри в один рядок, не стовпчик із семи. До M25 тут була таблиця з
/// податками, схемою розподілу і двома розкривачками: щоб дізнатись «скільки лишилось»,
/// доводилось прочитати весь місяць. Тепер картка відповідає одним рядком, а те, що
/// потрібно раз на місяць, живе на своїх екранах (податки — на екрані податків, кошики —
/// на розподілі), де воно й так є повністю.
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
        {/* Коли бюджет іде від порахованого залишку, заголовок веде туди, де цю суму можна
            перерахувати або прибрати. Доти це просто межі періоду — тиснути нема на що. */}
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

      {/* Податки лишаються у валюті рушія: це цифри для книгової, і сума з міткою гривні
          не збіглася б ні з одним документом. Розклад по VAT/ZUS — на екрані податків. */}
      {taxes && (
        <p className="text-xs text-neutral-400 tabular-nums">
          Прийшло {money(taxes.gross, taxes.currency)} · на податки{' '}
          {money(taxes.setAside, taxes.currency)}
        </p>
      )}
    </div>
  )
}

/// Скільки місячна арифметика тримає в банках. Одним рядком, а не по кошиках: вже
/// відкладене вручну і те, що ще тримається з бюджету, — це та сама зарезервована сума,
/// і два рядки читались би як подвійне утримання.
function heldBack(summary: SafeToSpend): number {
  return summary.envelopes.reduce((s, e) => s + e.depositedThisMonth + e.stillToReserve, 0)
}

/// Банки get their own card, not lines in the period summary: a balance that survives
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
                  {/* Схема відкладає сама, тож зазвичай план виконаний. Суму тут не
                      повторюємо: вона вже стоїть праворуч у тому ж рядку, і два однакові
                      числа поруч не влізали в рядок на телефоні. */}
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
              // Дохід теж відкривається — формою доходу, бо в ньому ще є VAT. Раніше тап по
              // рядку доходу нічого не робив, і виправляти рахунок доводилось видаленням і
              // повторним уведенням — а саме там і губиться цифра.
              onClick={() => onEdit(t)}
              className="flex-1 min-w-0 text-left"
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
