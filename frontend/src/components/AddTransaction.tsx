import { useState } from 'react'
import type { Category, Priority, SaveCategory, SaveIncome, SaveTransaction, Transaction } from '../types'
import { CURRENCIES, shiftIso, todayIso } from '../types'
import { readLastUsed, writeLastUsed } from '../lastUsed'

interface Props {
  categories: Category[]
  onSave: (tx: SaveTransaction) => Promise<void>
  onSaveIncome: (income: SaveIncome) => Promise<void>
  onCreateCategory: (c: SaveCategory) => Promise<Category>
  onCancel: () => void
  /// When set, the form edits this transaction instead of creating a new one.
  editing?: Transaction | null
}

const PRIORITIES: Priority[] = ['Must', 'Should', 'Want']
const PRIORITY_LABEL: Record<Priority, string> = { Must: 'Треба', Should: 'Варто', Want: 'Хочу' }

export function AddTransaction({ categories, onSave, onSaveIncome, onCreateCategory, onCancel, editing }: Props) {
  const [newCatOpen, setNewCatOpen] = useState(false)
  const [newCatName, setNewCatName] = useState('')
  const [newCatIcon, setNewCatIcon] = useState('')
  const last = readLastUsed()
  const [isIncome, setIsIncome] = useState(false)
  const [amount, setAmount] = useState(editing ? String(editing.amountOriginal) : '')
  // Open pre-filled with what was used last time — fewer taps per entry.
  const [currency, setCurrency] = useState(editing?.currencyOriginal ?? last.currency ?? 'PLN')
  const [categoryId, setCategoryId] = useState<number | null>(
    editing?.categoryId ?? categories.find((c) => c.id === last.categoryId)?.id ?? categories[0]?.id ?? null,
  )
  const [date, setDate] = useState(editing?.date ?? todayIso())
  const [priority, setPriority] = useState<Priority>(editing?.priority ?? 'Should')
  const [includesVat, setIncludesVat] = useState(true)
  const [note, setNote] = useState(editing?.note ?? '')
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
          date,
          note: note.trim() || null,
        })
        writeLastUsed({ categoryId: categoryId!, currency })
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
        <h1 className="text-lg font-semibold">
          {editing ? 'Редагувати' : isIncome ? 'Новий дохід' : 'Нова транзакція'}
        </h1>
      </div>

      {/* Expense / income switch — not offered while editing an existing row */}
      <div className={`flex gap-2 ${editing ? 'hidden' : ''}`}>
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
                <button
                  onClick={() => setNewCatOpen((o) => !o)}
                  className="rounded-xl px-3 py-2 text-sm border border-dashed border-neutral-300 dark:border-neutral-700 text-neutral-500"
                >
                  + Нова
                </button>
              </div>

              {newCatOpen && (
                <div className="mt-2 flex gap-2">
                  <input
                    autoFocus
                    placeholder="🍕"
                    value={newCatIcon}
                    onChange={(e) => setNewCatIcon(e.target.value)}
                    className="w-14 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-2 py-2 text-center text-sm outline-none"
                  />
                  <input
                    placeholder="Назва категорії"
                    value={newCatName}
                    onChange={(e) => setNewCatName(e.target.value)}
                    className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
                  />
                  <button
                    disabled={!newCatName.trim()}
                    onClick={async () => {
                      try {
                        const created = await onCreateCategory({
                          name: newCatName.trim(),
                          icon: newCatIcon.trim() || null,
                        })
                        setCategoryId(created.id)   // pick it right away — zero extra taps
                        setNewCatName('')
                        setNewCatIcon('')
                        setNewCatOpen(false)
                      } catch (e) {
                        setError(e instanceof Error ? e.message : 'Не вдалося створити')
                      }
                    }}
                    className="rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-4 text-sm font-medium disabled:opacity-40"
                  >
                    OK
                  </button>
                </div>
              )}
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

        {/* Date — logging yesterday must be as easy as today */}
        {!isIncome && (
          <div>
            <label className="text-xs text-neutral-400">Коли</label>
            <div className="mt-1 flex gap-2 items-center">
              {[
                { label: 'Сьогодні', value: todayIso() },
                { label: 'Вчора', value: shiftIso(todayIso(), -1) },
              ].map((o) => (
                <button
                  key={o.label}
                  onClick={() => setDate(o.value)}
                  className={`rounded-xl px-3 py-2 text-sm ${
                    date === o.value
                      ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                      : 'bg-neutral-100 dark:bg-neutral-800'
                  }`}
                >
                  {o.label}
                </button>
              ))}
              <input
                type="date"
                value={date}
                max={todayIso()}
                onChange={(e) => setDate(e.target.value)}
                className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
              />
            </div>
          </div>
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
        {saving ? 'Зберігаю…' : editing ? 'Зберегти зміни' : 'Зберегти'}
      </button>
    </div>
  )
}
