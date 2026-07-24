import { useState } from 'react'
import type { Budget } from '../types'

interface Props {
  budget: Budget | null
  onSave: (amount: number) => Promise<void>
  onBack: () => void
}

export function Settings({ budget, onSave, onBack }: Props) {
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
        <label className="text-sm text-neutral-500">Місячний бюджет (PLN)</label>
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

      <p className="text-xs text-neutral-400 text-center">
        Safe-to-spend рахується від цього бюджета. Банківський синк — у майбутніх версіях.
      </p>
    </div>
  )
}
