import type { MonthTaxes, SafeToSpend, SavingsSummary, Transaction } from '../types'
import { BASE_CURRENCY } from '../types'
import { money } from '../format'
import { buildQuickActions, type QuickAction } from '../quickRepeat'

interface Props {
  summary: SafeToSpend | null
  transactions: Transaction[]
  onDelete: (id: number) => void
  onGoSettings: () => void
  onQuickRepeat: (a: QuickAction) => void
  onEdit: (t: Transaction) => void
  onGoSavings: () => void
}

export function Home({ summary, transactions, onDelete, onGoSettings, onQuickRepeat, onEdit, onGoSavings }: Props) {
  const quick = buildQuickActions(transactions, (name) => ICONS[name] ?? '📦')

  return (
    <div className="space-y-6">
      <SafeToSpendCard summary={summary} onGoSettings={onGoSettings} />
      {summary?.monthTaxes && <MonthSummary summary={summary} taxes={summary.monthTaxes} />}
      {summary && <SavingsCard savings={summary.savings} currency={summary.currency} onOpen={onGoSavings} />}
      {quick.length > 0 && <QuickRow actions={quick} onPick={onQuickRepeat} />}
      <RecentList transactions={transactions} onDelete={onDelete} onEdit={onEdit} />
    </div>
  )
}

/// One-tap repeat of a recent expense — the cheapest possible way to log something.
function QuickRow({ actions, onPick }: { actions: QuickAction[]; onPick: (a: QuickAction) => void }) {
  return (
    <div>
      <h2 className="text-sm font-medium text-neutral-400 mb-2 px-1">Швидко — один дотик</h2>
      <div className="flex gap-2 flex-wrap">
        {actions.map((a) => (
          <button
            key={a.key}
            onClick={() => onPick(a)}
            className="flex-1 min-w-[30%] rounded-xl bg-white dark:bg-neutral-900 px-3 py-3 shadow-sm text-left"
          >
            <span className="text-lg">{a.icon}</span>
            <p className="text-xs text-neutral-400 truncate">{a.categoryName}</p>
            <p className="font-semibold tabular-nums text-sm">{money(a.amount, a.currency)}</p>
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
      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center">
        <p className="text-neutral-500">Бюджет ще не заданий.</p>
        <button
          onClick={onGoSettings}
          className="mt-3 rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-4 py-2 font-medium"
        >
          Задати місячний бюджет
        </button>
      </div>
    )
  }

  const value = summary.safeToSpendToday ?? 0
  const positive = value >= 0

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center">
      <p className="text-sm uppercase tracking-wide text-neutral-400">Безпечно сьогодні</p>
      <p className={`mt-1 text-5xl font-bold tabular-nums ${positive ? 'text-emerald-600' : 'text-red-600'}`}>
        {money(value, summary.currency)}
      </p>
      <p className="mt-3 text-sm text-neutral-500">
        Залишок {money(summary.remainingThisMonth ?? 0, summary.currency)} · {summary.daysLeftInMonth} дн.
      </p>
      <p className="text-xs text-neutral-400">
        Витрачено {money(summary.spentThisMonth, summary.currency)} з {money(summary.monthlyBudget ?? 0, summary.currency)}
      </p>
      {summary.reservedRecurring > 0 && !summary.monthTaxes && (
        <p className="text-xs text-neutral-400">
          Зарезервовано на фіксовані: {money(summary.reservedRecurring, summary.currency)}
        </p>
      )}
    </div>
  )
}

/// Answers "чому на рахунку більше, ніж бюджет": every row between what landed on the
/// account and what is left is spelled out, so the column actually adds up.
function MonthSummary({ summary, taxes }: { summary: SafeToSpend; taxes: MonthTaxes }) {
  const c = summary.currency

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm">
      <h2 className="text-sm font-medium text-neutral-400 mb-3">Підсумок місяця</h2>
      <dl className="space-y-1.5 text-sm">
        <Row label="Прийшло на рахунок" value={money(taxes.gross, c)} />
        <Row label="Відкладено на податки" value={`− ${money(taxes.setAside, c)}`} muted />
        <details className="group">
          <summary className="cursor-pointer list-none text-xs text-neutral-400 pl-3 py-1">
            з чого · VAT, ZUS, здоровотна, податок
          </summary>
          <div className="pl-3 pt-1 space-y-1 text-xs text-neutral-400">
            <Row label="VAT" value={money(taxes.vat, c)} small />
            <Row label="ZUS соціальний" value={money(taxes.zusSocial, c)} small />
            <Row label="Здоровотна" value={money(taxes.health, c)} small />
            <Row label="Податок (ryczałt)" value={money(taxes.tax, c)} small />
          </div>
        </details>
        <Row label="Бюджет місяця" value={money(taxes.takeHome, c)} strong />
        <Row label="Витрачено" value={`− ${money(summary.spentThisMonth, c)}`} muted />
        {summary.reservedRecurring > 0 && (
          <Row label="Зарезервовано на фіксовані" value={`− ${money(summary.reservedRecurring, c)}`} muted />
        )}
        {summary.savings.stillToReserve > 0 && (
          <Row label="Ще відкласти цього місяця" value={`− ${money(summary.savings.stillToReserve, c)}`} muted />
        )}
        <Row label="Лишилось" value={money(summary.remainingThisMonth ?? 0, c)} strong />
      </dl>
    </div>
  )
}

/// The envelope gets its own card, not a line in the summary: Bohdan asked to see it
/// separately, and a balance that survives across months is a different kind of number
/// from this month's arithmetic.
function SavingsCard({ savings, currency, onOpen }: {
  savings: SavingsSummary; currency: string; onOpen: () => void
}) {
  const nothingSetUp = savings.balance === 0 && savings.monthGoal === 0
  if (nothingSetUp) {
    return (
      <button
        onClick={onOpen}
        className="w-full rounded-2xl border border-dashed border-neutral-300 dark:border-neutral-700 p-4 text-sm text-neutral-500"
      >
        + Відкладати щомісяця
      </button>
    )
  }

  const goalMet = savings.monthGoal > 0 && savings.stillToReserve === 0

  return (
    <button onClick={onOpen} className="w-full rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm text-left">
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-sm text-neutral-400">Відкладено</span>
        <span className="text-2xl font-bold tabular-nums">{money(savings.balance, currency)}</span>
      </div>
      {savings.monthGoal > 0 && (
        <p className="mt-1 text-xs text-neutral-400">
          {goalMet
            ? `Ціль місяця ${money(savings.monthGoal, currency)} — виконано ✓`
            : `Цього місяця ${money(savings.depositedThisMonth, currency)} з ${money(savings.monthGoal, currency)}`}
        </p>
      )}
    </button>
  )
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
              </p>
              <p className="text-xs text-neutral-400 truncate">
                {t.note || t.merchant || t.date}
              </p>
            </button>
            <div className="text-right">
              <p className={`font-semibold tabular-nums ${t.kind === 'Income' ? 'text-emerald-600' : ''}`}>
                {t.kind === 'Income' ? '+' : ''}{money(t.amountOriginal, t.currencyOriginal)}
              </p>
              {t.currencyOriginal !== BASE_CURRENCY && (
                <p className="text-xs text-neutral-400 tabular-nums">≈ {money(t.amountBase, BASE_CURRENCY)}</p>
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
