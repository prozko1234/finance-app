import { useState } from 'react'
import type { Category, Recurring as RecurringType, SaveRecurring } from '../types'
import { CURRENCIES } from '../types'
import { money } from '../format'
import { ScreenHeader } from './ScreenHeader'

interface Props {
  categories: Category[]
  items: RecurringType[]
  onCreate: (r: SaveRecurring) => Promise<void>
  onToggle: (r: RecurringType) => void
  onDelete: (id: number) => Promise<void>
  onBack: () => void
}

/// A row waiting to be saved. `key` is local-only: drafts have no server id yet,
/// and using the array index would make removals re-render the wrong row.
interface Draft extends SaveRecurring {
  key: number
  categoryName: string
}

let nextDraftKey = 1

export function Recurring({ categories, items, onCreate, onToggle, onDelete, onBack }: Props) {
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('PLN')
  const [categoryId, setCategoryId] = useState<number | null>(categories[0]?.id ?? null)
  const [day, setDay] = useState('1')
  const [note, setNote] = useState('')
  const [drafts, setDrafts] = useState<Draft[]>([])
  /// Ids marked for deletion. Nothing is sent until the confirm bar is used —
  /// a mis-tap on a small ✕ must not silently drop a subscription.
  const [marked, setMarked] = useState<number[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amountNum = Number(amount.replace(',', '.'))
  const dayNum = Number(day)
  const valid = amountNum > 0 && categoryId !== null && dayNum >= 1 && dayNum <= 31
  const pending = drafts.length + (valid ? 1 : 0)

  /// Currency, category and day usually repeat across a batch; the amount and the
  /// name never do, so only those are cleared between rows.
  function stage(): Draft[] | null {
    if (!valid || categoryId === null) return null
    const row: Draft = {
      key: nextDraftKey++,
      amount: amountNum,
      currency,
      categoryId,
      categoryName: categories.find((c) => c.id === categoryId)?.name ?? '',
      dayOfMonth: dayNum,
      note: note.trim() || null,
      active: true,
    }
    setAmount('')
    setNote('')
    return [...drafts, row]
  }

  function addAnother() {
    const staged = stage()
    if (staged) setDrafts(staged)
  }

  /// One row at a time, keeping only what failed: retrying after a partial failure
  /// must not create a second copy of the rows that already went through.
  async function saveAll() {
    const rows = stage() ?? drafts
    if (rows.length === 0) return
    setSaving(true)
    setError(null)
    let left = rows
    try {
      for (const row of rows) {
        const { key: _key, categoryName: _name, ...payload } = row
        await onCreate(payload)
        left = left.filter((r) => r.key !== row.key)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setDrafts(left)
      setSaving(false)
    }
  }

  async function deleteMarked() {
    setSaving(true)
    setError(null)
    let left = marked
    try {
      for (const id of marked) {
        await onDelete(id)
        left = left.filter((x) => x !== id)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося видалити')
    } finally {
      setMarked(left)
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <ScreenHeader title="Регулярні: підписки й дохід" onBack={onBack} />

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

        {drafts.length > 0 && (
          <ul className="space-y-1">
            {drafts.map((d) => (
              <li key={d.key} className="flex items-center gap-2 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm">
                <span className="flex-1 min-w-0 truncate">{d.note || d.categoryName}</span>
                <span className="text-xs text-neutral-400">{d.dayOfMonth}-го</span>
                <span className="font-medium tabular-nums">{money(d.amount, d.currency)}</span>
                <button
                  onClick={() => setDrafts(drafts.filter((x) => x.key !== d.key))}
                  className="text-neutral-400 hover:text-red-500 px-1" aria-label="Прибрати з черги"
                >
                  ✕
                </button>
              </li>
            ))}
          </ul>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}

        <div className="flex gap-2">
          <button
            onClick={addAnother} disabled={!valid || saving}
            className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2.5 font-medium disabled:opacity-40"
          >
            + Ще одна
          </button>
          <button
            onClick={saveAll} disabled={pending === 0 || saving}
            className="flex-1 rounded-xl bg-emerald-600 text-white py-2.5 font-medium disabled:opacity-40"
          >
            {saving ? 'Зберігаю…' : pending > 1 ? `Зберегти (${pending})` : 'Зберегти'}
          </button>
        </div>
      </div>

      {items.length === 0 ? (
        <p className="text-center text-neutral-400 text-sm">Ще немає нічого регулярного.</p>
      ) : (
        <ul className="space-y-2">
          {items.map((r) => {
            const isMarked = marked.includes(r.id)
            return (
              <li
                key={r.id}
                className={`flex items-center gap-3 rounded-xl px-4 py-3 shadow-sm ${
                  isMarked ? 'bg-red-50 dark:bg-red-950/40' : 'bg-white dark:bg-neutral-900'
                } ${r.active || isMarked ? '' : 'opacity-50'}`}
              >
                <div className="flex-1 min-w-0">
                  <p className={`font-medium truncate ${isMarked ? 'line-through text-neutral-400' : ''}`}>
                    {r.note || r.categoryName}
                  </p>
                  <p className="text-xs text-neutral-400">
                    кожного {r.dayOfMonth}-го · {r.kind === 'Income' ? 'дохід' : r.categoryName}
                  </p>
                </div>
                <p className={`font-semibold tabular-nums ${isMarked ? 'text-neutral-400 line-through' : r.kind === 'Income' ? 'text-emerald-600' : ''}`}>
                  {r.kind === 'Income' ? '+' : ''}{money(r.amountOriginal, r.currencyOriginal)}
                </p>
                <button
                  onClick={() => onToggle(r)} disabled={isMarked}
                  className="text-sm text-neutral-400 px-1 disabled:opacity-30"
                  title={r.active ? 'Призупинити' : 'Відновити'}
                >
                  {r.active ? '⏸' : '▶'}
                </button>
                <button
                  onClick={() => setMarked(isMarked ? marked.filter((id) => id !== r.id) : [...marked, r.id])}
                  className={`px-1 ${isMarked ? 'text-neutral-500' : 'text-neutral-300 hover:text-red-500'}`}
                  aria-label={isMarked ? 'Не видаляти' : 'Позначити на видалення'}
                >
                  {isMarked ? '↺' : '✕'}
                </button>
              </li>
            )
          })}
        </ul>
      )}

      {marked.length > 0 && (
        <div className="sticky bottom-4 flex items-center gap-2 rounded-2xl bg-white dark:bg-neutral-900 p-3 shadow-lg">
          <p className="flex-1 text-sm">Видалити {marked.length}?</p>
          <button onClick={() => setMarked([])} className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2 text-sm font-medium">
            Скасувати
          </button>
          <button
            onClick={deleteMarked} disabled={saving}
            className="rounded-xl bg-red-600 text-white px-4 py-2 text-sm font-medium disabled:opacity-40"
          >
            {saving ? 'Видаляю…' : 'Видалити'}
          </button>
        </div>
      )}
    </div>
  )
}
