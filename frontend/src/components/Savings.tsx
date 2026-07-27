import { useState } from 'react'
import type { Savings as SavingsData, SaveSavingsPlan } from '../types'
import { money } from '../format'
import { todayIso } from '../types'

interface Props {
  data: SavingsData | null
  onSavePlan: (p: SaveSavingsPlan) => Promise<void>
  onAddEntry: (kind: 'Deposit' | 'Withdrawal', amount: number, note: string | null) => Promise<void>
  onDeleteEntry: (id: number) => Promise<void>
  onBack: () => void
}

export function Savings({ data, onSavePlan, onAddEntry, onDeleteEntry, onBack }: Props) {
  if (!data) return <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm animate-pulse h-40" />

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onBack} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">Заощадження</h1>
      </div>

      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center">
        <p className="text-sm uppercase tracking-wide text-neutral-400">У заощадженнях</p>
        <p className="mt-1 text-4xl font-bold tabular-nums">{money(data.balance, data.currency)}</p>
        {data.monthGoal > 0 && (
          <p className="mt-2 text-xs text-neutral-400">
            Цього місяця {money(data.depositedThisMonth, data.currency)} з {money(data.monthGoal, data.currency)}
            {data.stillToReserve > 0 && ` · ще ${money(data.stillToReserve, data.currency)} з бюджету тримається тут`}
          </p>
        )}
      </div>

      <MoveMoney currency={data.currency} balance={data.balance} onAdd={onAddEntry} />
      <PlanForm data={data} onSave={onSavePlan} />
      <History data={data} onDelete={onDeleteEntry} />
    </div>
  )
}

/// Manual movement. Deposits count towards this month's goal rather than stacking on top
/// of it, so putting money aside by hand never costs safe-to-spend twice.
function MoveMoney({ currency, balance, onAdd }: {
  currency: string
  balance: number
  onAdd: (kind: 'Deposit' | 'Withdrawal', amount: number, note: string | null) => Promise<void>
}) {
  const [amount, setAmount] = useState('')
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const value = Number(amount.replace(',', '.'))
  const valid = value > 0

  async function move(kind: 'Deposit' | 'Withdrawal') {
    if (!valid || busy) return
    setBusy(true)
    setError(null)
    try {
      await onAdd(kind, value, note.trim() || null)
      setAmount('')
      setNote('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-3">
      <h2 className="text-sm font-medium text-neutral-400">Змінити вручну</h2>

      <input
        type="text"
        inputMode="decimal"
        placeholder="0"
        value={amount}
        onChange={(e) => setAmount(e.target.value)}
        className="w-full text-3xl font-bold tabular-nums bg-transparent outline-none"
      />
      <input
        type="text"
        placeholder="Нотатка (необов'язково)"
        value={note}
        onChange={(e) => setNote(e.target.value)}
        className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
      />

      {error && <p className="text-sm text-red-600">{error}</p>}

      <div className="flex gap-2">
        <button
          disabled={!valid || busy}
          onClick={() => move('Deposit')}
          className="flex-1 rounded-xl bg-emerald-600 text-white px-3 py-2.5 font-medium disabled:opacity-40"
        >
          + Відкласти
        </button>
        <button
          disabled={!valid || busy || value > balance}
          onClick={() => move('Withdrawal')}
          className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 font-medium disabled:opacity-40"
        >
          − Зняти
        </button>
      </div>
      <p className="text-xs text-neutral-400">
        Заощадження не входять у «Ще сьогодні». Зняти можна будь-коли — це твої гроші,
        не податки. Максимум до зняття: {money(balance, currency)}.
      </p>
    </div>
  )
}

function PlanForm({ data, onSave }: { data: SavingsData; onSave: (p: SaveSavingsPlan) => Promise<void> }) {
  const [mode, setMode] = useState<'Fixed' | 'Percent'>(data.mode)
  const [value, setValue] = useState(data.value > 0 ? String(data.value) : '')
  const [active, setActive] = useState(data.active)
  const [busy, setBusy] = useState(false)

  const num = Number(value.replace(',', '.'))
  const valid = num >= 0 && (mode !== 'Percent' || num <= 100)

  async function save() {
    if (!valid || busy) return
    setBusy(true)
    try {
      await onSave({ mode, value: num, active })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-3">
      <h2 className="text-sm font-medium text-neutral-400">Скільки у заощадження щомісяця</h2>

      <div className="flex gap-2">
        {(['Fixed', 'Percent'] as const).map((m) => (
          <button
            key={m}
            onClick={() => setMode(m)}
            className={`flex-1 rounded-xl px-3 py-2 text-sm ${
              mode === m
                ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                : 'bg-neutral-100 dark:bg-neutral-800'
            }`}
          >
            {m === 'Fixed' ? 'Сума' : '% від бюджету'}
          </button>
        ))}
      </div>

      <div className="flex items-center gap-2">
        <input
          type="text"
          inputMode="decimal"
          placeholder="0"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          className="flex-1 text-2xl font-bold tabular-nums bg-transparent outline-none"
        />
        <span className="text-neutral-400">{mode === 'Fixed' ? data.currency : '%'}</span>
      </div>

      <label className="flex items-center gap-2 text-sm text-neutral-500">
        <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} />
        Відкладати автоматично
      </label>

      {mode === 'Percent' && (
        <p className="text-xs text-neutral-400">
          Відсоток рахується від бюджета місяця — уже після податків.
          Поки доходу немає, ціль нульова.
        </p>
      )}

      <button
        disabled={!valid || busy}
        onClick={save}
        className="w-full rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-3 py-2.5 font-medium disabled:opacity-40"
      >
        Зберегти
      </button>
    </div>
  )
}

function History({ data, onDelete }: { data: SavingsData; onDelete: (id: number) => Promise<void> }) {
  if (data.recent.length === 0) return null

  return (
    <div>
      <h2 className="text-sm font-medium text-neutral-400 mb-2 px-1">Рухи</h2>
      <ul className="space-y-2">
        {data.recent.map((e) => (
          <li key={e.id} className="flex items-center gap-3 rounded-xl bg-white dark:bg-neutral-900 px-4 py-3 shadow-sm">
            <span className="text-xl">{e.kind === 'Deposit' ? '🐖' : '↩️'}</span>
            <div className="flex-1 min-w-0">
              <p className="font-medium truncate">{e.kind === 'Deposit' ? 'У заощадження' : 'Знято'}</p>
              <p className="text-xs text-neutral-400 truncate">{e.note || (e.date === todayIso() ? 'сьогодні' : e.date)}</p>
            </div>
            <p className={`font-semibold tabular-nums ${e.kind === 'Deposit' ? 'text-emerald-600' : ''}`}>
              {e.kind === 'Deposit' ? '+' : '−'}{money(e.amount, data.currency)}
            </p>
            <button onClick={() => onDelete(e.id)} className="text-neutral-300 hover:text-red-500 px-1" aria-label="Видалити">
              ✕
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
