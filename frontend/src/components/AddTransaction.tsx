import { useState } from 'react'
import type { Category, Priority, SaveIncome, SaveTransaction } from '../types'
import { CURRENCIES } from '../types'

interface Props {
  categories: Category[]
  onSave: (tx: SaveTransaction) => Promise<void>
  onSaveIncome: (income: SaveIncome) => Promise<void>
  onCancel: () => void
}

const PRIORITIES: Priority[] = ['Must', 'Should', 'Want']
const PRIORITY_LABEL: Record<Priority, string> = { Must: 'Треба', Should: 'Варто', Want: 'Хочу' }

export function AddTransaction({ categories, onSave, onSaveIncome, onCancel }: Props) {
  const [isIncome, setIsIncome] = useState(false)
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('PLN')
  const [categoryId, setCategoryId] = useState<number | null>(categories[0]?.id ?? null)
  const [priority, setPriority] = useState<Priority>('Should')
  const [includesVat, setIncludesVat] = useState(true)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amountNum = Number(amount.replace(',', '.'))
  const valid = amountNum > 0 && (isIncome || categoryId !== null)

  async function submit() {
    if (!valid) return
    setSaving(true)
    setError(null)
    try {
      if (isIncome) {
        await onSaveIncome({
          amount: amountNum,
          amountIncludesVat: includesVat,
          currency,
          note: note.trim() || null,
        })
      } else {
        await onSave({
          amount: amountNum,
          currency,
          categoryId: categoryId!,
          priority,
          frequency: 'OneOff',
          note: note.trim() || null,
        })
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
      setSaving(false)
    }
  }

  const accent = isIncome ? 'bg-emerald-600' : 'bg-emerald-600'

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onCancel} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">{isIncome ? 'Новий дохід' : 'Нова транзакція'}</h1>
      </div>

      {/* Expense / income switch */}
      <div className="flex gap-2">
        {[false, true].map((v) => (
          <button
            key={String(v)}
            onClick={() => setIsIncome(v)}
            className={`flex-1 rounded-xl px-3 py-2.5 text-sm font-medium ${
              isIncome === v
                ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                : 'bg-neutral-100 dark:bg-neutral-800'
            }`}
          >
            {v ? '↓ Дохід' : '↑ Витрата'}
          </button>
        ))}
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

        {isIncome ? (
          <>
            <div>
              <label className="text-xs text-neutral-400">Що прийшло</label>
              <div className="mt-1 flex gap-2">
                {[true, false].map((v) => (
                  <button
                    key={String(v)}
                    onClick={() => setIncludesVat(v)}
                    className={`flex-1 rounded-xl px-3 py-2 text-sm ${
                      includesVat === v
                        ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                        : 'bg-neutral-100 dark:bg-neutral-800'
                    }`}
                  >
                    {v ? 'з VAT (brutto)' : 'без VAT (netto)'}
                  </button>
                ))}
              </div>
            </div>
            <p className="text-xs text-neutral-400">
              VAT відділиться автоматично, а податки за місяць порахуються від сумарного
              доходу — так бюджет показує реальні гроші.
            </p>
          </>
        ) : (
          <>
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
          </>
        )}

        {/* Note */}
        <input
          type="text"
          placeholder={isIncome ? 'Від кого / за що' : "Нотатка (необов'язково)"}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>

      <button
        onClick={submit}
        disabled={!valid || saving}
        className={`w-full rounded-2xl ${accent} text-white py-4 font-semibold disabled:opacity-40`}
      >
        {saving ? 'Зберігаю…' : 'Зберегти'}
      </button>
    </div>
  )
}
