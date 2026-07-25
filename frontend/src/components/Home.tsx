import type { SafeToSpend, Transaction } from '../types'
import { BASE_CURRENCY } from '../types'
import { money } from '../format'

interface Props {
  summary: SafeToSpend | null
  transactions: Transaction[]
  onDelete: (id: number) => void
  onGoSettings: () => void
}

export function Home({ summary, transactions, onDelete, onGoSettings }: Props) {
  return (
    <div className="space-y-6">
      <SafeToSpendCard summary={summary} onGoSettings={onGoSettings} />
      <RecentList transactions={transactions} onDelete={onDelete} />
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
      {summary.reservedRecurring > 0 && (
        <p className="text-xs text-neutral-400">
          Зарезервовано на фіксовані: {money(summary.reservedRecurring, summary.currency)}
        </p>
      )}
    </div>
  )
}

function RecentList({ transactions, onDelete }: { transactions: Transaction[]; onDelete: (id: number) => void }) {
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
            <div className="flex-1 min-w-0">
              <p className="font-medium truncate">
                {t.kind === 'Income' ? 'Дохід' : t.categoryName}
              </p>
              <p className="text-xs text-neutral-400 truncate">
                {t.note || t.merchant || t.date}
              </p>
            </div>
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

function iconFor(t: Transaction): string {
  const map: Record<string, string> = {
    Їжа: '🍽', Транспорт: '🚌', Житло: '🏠', "Здоров'я": '💊', Розваги: '🎮', Інше: '📦',
  }
  return map[t.categoryName] ?? '📦'
}
