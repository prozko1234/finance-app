import { useState } from 'react'
import type { Category, Recurring as RecurringType, SaveRecurring } from '../types'
import { CURRENCIES } from '../types'
import { money } from '../format'

interface Props {
  categories: Category[]
  items: RecurringType[]
  onCreate: (r: SaveRecurring) => Promise<void>
  onToggle: (r: RecurringType) => void
  onDelete: (id: number) => void
  onBack: () => void
}

export function Recurring({ categories, items, onCreate, onToggle, onDelete, onBack }: Props) {
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('PLN')
  const [categoryId, setCategoryId] = useState<number | null>(categories[0]?.id ?? null)
  const [day, setDay] = useState('1')
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amountNum = Number(amount.replace(',', '.'))
  const dayNum = Number(day)
  const valid = amountNum > 0 && categoryId !== null && dayNum >= 1 && dayNum <= 31

  async function add() {
    if (!valid || categoryId === null) return
    setSaving(true)
    setError(null)
    try {
      await onCreate({ amount: amountNum, currency, categoryId, dayOfMonth: dayNum, note: note.trim() || null, active: true })
      setAmount('')
      setNote('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося додати')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onBack} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">Регулярні: підписки й дохід</h1>
      </div>

      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-3">
        <div className="flex gap-2">
          <input
            inputMode="decimal" placeholder="0" value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="flex-1 text-2xl font-bold tabular-nums bg-transparent outline-none"
          />
          <select
            value={currency} onChange={(e) => setCurrency(e.target.value)}
            className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 font-medium"
          >
            {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        <div className="flex gap-2 flex-wrap">
          {categories.map((c) => (
            <button
              key={c.id} onClick={() => setCategoryId(c.id)}
              className={`rounded-xl px-3 py-1.5 text-sm ${
                categoryId === c.id
                  ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                  : 'bg-neutral-100 dark:bg-neutral-800'
              }`}
            >
              {c.icon} {c.name}
            </button>
          ))}
        </div>

        <div className="flex items-center gap-2 text-sm">
          <span className="text-neutral-500">кожного</span>
          <input
            inputMode="numeric" value={day} onChange={(e) => setDay(e.target.value)}
            className="w-14 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-2 py-1 text-center"
          />
          <span className="text-neutral-500">числа</span>
        </div>

        <input
          placeholder="Назва (Netflix, оренда…)" value={note}
          onChange={(e) => setNote(e.target.value)}
          className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />

        {error && <p className="text-sm text-red-600">{error}</p>}

        <button
          onClick={add} disabled={!valid || saving}
          className="w-full rounded-xl bg-emerald-600 text-white py-2.5 font-medium disabled:opacity-40"
        >
          {saving ? 'Додаю…' : 'Додати підписку'}
        </button>
      </div>

      {items.length === 0 ? (
        <p className="text-center text-neutral-400 text-sm">Ще немає нічого регулярного.</p>
      ) : (
        <ul className="space-y-2">
          {items.map((r) => (
            <li
              key={r.id}
              className={`flex items-center gap-3 rounded-xl bg-white dark:bg-neutral-900 px-4 py-3 shadow-sm ${r.active ? '' : 'opacity-50'}`}
            >
              <div className="flex-1 min-w-0">
                <p className="font-medium truncate">{r.note || r.categoryName}</p>
                <p className="text-xs text-neutral-400">
                  кожного {r.dayOfMonth}-го · {r.kind === 'Income' ? 'дохід' : r.categoryName}
                </p>
              </div>
              <p className={`font-semibold tabular-nums ${r.kind === 'Income' ? 'text-emerald-600' : ''}`}>
                {r.kind === 'Income' ? '+' : ''}{money(r.amountOriginal, r.currencyOriginal)}
              </p>
              <button onClick={() => onToggle(r)} className="text-sm text-neutral-400 px-1" title={r.active ? 'Призупинити' : 'Відновити'}>
                {r.active ? '⏸' : '▶'}
              </button>
              <button onClick={() => onDelete(r.id)} className="text-neutral-300 hover:text-red-500 px-1" aria-label="Видалити">✕</button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
