import { useState } from 'react'
import type { Budget } from '../types'
import { money } from '../format'

interface Props {
  budget: Budget | null
  /// This month's budget derived from income, when there is income. While it is set,
  /// the manual amount below is ignored — M17 stopped the UI from pretending otherwise.
  incomeBudget: number | null
  onSave: (amount: number) => Promise<void>
  onBack: () => void
  onGoRecurring: () => void
  onGoTax: () => void
  onGoCategories: () => void
  /// Only provided in a dev build — the API exposes these endpoints in Development only.
  onGoDev?: () => void
}

export function Settings({ budget, incomeBudget, onSave, onBack, onGoRecurring, onGoTax, onGoCategories, onGoDev }: Props) {
  const [amount, setAmount] = useState(budget?.monthlyAmount != null ? String(budget.monthlyAmount) : '')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amountNum = Number(amount.replace(',', '.'))
  const valid = amountNum >= 0 && amount.trim() !== ''

  async function submit() {
    if (!valid) return
    setSaving(true)
    setError(null)
    setSaved(false)
    try {
      await onSave(amountNum)
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onBack} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">Налаштування</h1>
      </div>

      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-5 shadow-sm space-y-3">
        <label className="text-sm text-neutral-500">Запасний бюджет (PLN)</label>
        <p className="text-xs text-neutral-400">
          {incomeBudget !== null
            ? `Цього місяця не діє: бюджет уже порахований з доходу — ${money(incomeBudget, budget?.currency ?? 'PLN')}. Ця сума спрацює в місяці без доходу.`
            : 'Діє, поки за місяць немає доходу. Щойно впишеш дохід, бюджет порахується з нього.'}
        </p>
        <div className="flex gap-2 items-baseline">
          <input
            type="text"
            inputMode="decimal"
            placeholder="0"
            value={amount}
            onChange={(e) => { setAmount(e.target.value); setSaved(false) }}
            className="flex-1 text-3xl font-bold tabular-nums bg-transparent outline-none"
          />
          <span className="text-neutral-400 font-medium">zł</span>
        </div>
        {error && <p className="text-sm text-red-600">{error}</p>}
        {saved && <p className="text-sm text-emerald-600">Збережено ✓</p>}
      </div>

      <button
        onClick={submit}
        disabled={!valid || saving}
        className="w-full rounded-2xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 py-4 font-semibold disabled:opacity-40"
      >
        {saving ? 'Зберігаю…' : 'Зберегти бюджет'}
      </button>

      <button
        onClick={onGoRecurring}
        className="w-full flex items-center justify-between rounded-2xl bg-white dark:bg-neutral-900 px-5 py-4 shadow-sm"
      >
        <span className="font-medium">Підписки й регулярні</span>
        <span className="text-neutral-400">→</span>
      </button>

      <button
        onClick={onGoCategories}
        className="w-full flex items-center justify-between rounded-2xl bg-white dark:bg-neutral-900 px-5 py-4 shadow-sm"
      >
        <span className="font-medium">Категорії</span>
        <span className="text-neutral-400">→</span>
      </button>

      <button
        onClick={onGoTax}
        className="w-full flex items-center justify-between rounded-2xl bg-white dark:bg-neutral-900 px-5 py-4 shadow-sm"
      >
        <span className="font-medium">Податковий профіль</span>
        <span className="text-neutral-400">→</span>
      </button>

      {onGoDev && (
        <button
          onClick={onGoDev}
          className="w-full flex items-center justify-between rounded-2xl bg-white dark:bg-neutral-900 px-5 py-4 shadow-sm"
        >
          <span className="font-medium">Тестові дані</span>
          <span className="text-neutral-400">→</span>
        </button>
      )}

      <p className="text-xs text-neutral-400 text-center">
        «Ще сьогодні» рахується від бюджета місяця: з доходу, якщо він є, інакше — із
        запасного. Банківський синк — у майбутніх версіях.
      </p>
    </div>
  )
}
