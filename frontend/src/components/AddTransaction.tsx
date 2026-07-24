import { useState } from 'react'
import type { Category, Priority, SaveTransaction } from '../types'
import { CURRENCIES } from '../types'

interface Props {
  categories: Category[]
  onSave: (tx: SaveTransaction) => Promise<void>
  onCancel: () => void
}

const PRIORITIES: Priority[] = ['Must', 'Should', 'Want']
const PRIORITY_LABEL: Record<Priority, string> = { Must: 'Треба', Should: 'Варто', Want: 'Хочу' }

export function AddTransaction({ categories, onSave, onCancel }: Props) {
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('PLN')
  const [categoryId, setCategoryId] = useState<number | null>(categories[0]?.id ?? null)
  const [priority, setPriority] = useState<Priority>('Should')
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amountNum = Number(amount.replace(',', '.'))
  const valid = amountNum > 0 && categoryId !== null

  async function submit() {
    if (!valid || categoryId === null) return
    setSaving(true)
    setError(null)
    try {
      await onSave({
        amount: amountNum,
        currency,
        categoryId,
        priority,
        frequency: 'OneOff',
        note: note.trim() || null,
      })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onCancel} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">Нова транзакція</h1>
      </div>

      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-5 shadow-sm space-y-4">
        {/* Amount + currency */}
        <div className="flex gap-2">
          <input
            type="text"
            inputMode="decimal"
            autoFocus
            placeholder="0"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="flex-1 text-4xl font-bold tabular-nums bg-transparent outline-none w-full"
          />
          <select
            value={currency}
            onChange={(e) => setCurrency(e.target.value)}
            className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 font-medium"
          >
            {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        {/* Category */}
        <div>
          <label className="text-xs text-neutral-400">Категорія</label>
          <div className="mt-1 flex flex-wrap gap-2">
            {categories.map((c) => (
              <button
                key={c.id}
                onClick={() => setCategoryId(c.id)}
                className={`rounded-xl px-3 py-2 text-sm ${
                  categoryId === c.id
                    ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                    : 'bg-neutral-100 dark:bg-neutral-800'
                }`}
              >
                {c.icon} {c.name}
              </button>
            ))}
          </div>
        </div>

        {/* Priority */}
        <div>
          <label className="text-xs text-neutral-400">Пріоритет</label>
          <div className="mt-1 flex gap-2">
            {PRIORITIES.map((p) => (
              <button
                key={p}
                onClick={() => setPriority(p)}
                className={`flex-1 rounded-xl px-3 py-2 text-sm ${
                  priority === p
                    ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                    : 'bg-neutral-100 dark:bg-neutral-800'
                }`}
              >
                {PRIORITY_LABEL[p]}
              </button>
            ))}
          </div>
        </div>

        {/* Note */}
        <input
          type="text"
          placeholder="Нотатка (необов'язково)"
          value={note}
          onChange={(e) => setNote(e.target.value)}
          className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>

      <button
        onClick={submit}
        disabled={!valid || saving}
        className="w-full rounded-2xl bg-emerald-600 text-white py-4 font-semibold disabled:opacity-40"
      >
        {saving ? 'Зберігаю…' : 'Зберегти'}
      </button>
    </div>
  )
}
