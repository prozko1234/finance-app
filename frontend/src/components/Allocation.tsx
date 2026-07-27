import { useState } from 'react'
import type {
  Allocation as AllocationData, AllocationBucket, AllocationPreset, BucketKind, SaveAllocation,
} from '../types'
import { money } from '../format'

interface Props {
  data: AllocationData | null
  /// Бюджет місяця — щоб показувати не відсотки, а гроші. null = ще не заданий.
  budget: number | null
  currency: string
  onSave: (a: SaveAllocation) => Promise<void>
  onBack: () => void
}

const KINDS: { kind: BucketKind; label: string }[] = [
  { kind: 'Spending', label: 'На витрати' },
  { kind: 'Savings', label: 'Заощадження' },
  { kind: 'Investing', label: 'Інвестиції' },
  { kind: 'Debt', label: 'Борг' },
  { kind: 'Other', label: 'Інше' },
]

const KIND_LABEL = Object.fromEntries(KINDS.map((k) => [k.kind, k.label])) as Record<BucketKind, string>

export function Allocation({ data, budget, currency, onSave, onBack }: Props) {
  const [error, setError] = useState<string | null>(null)

  if (!data) return <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm animate-pulse h-40" />

  async function apply(a: SaveAllocation) {
    setError(null)
    try {
      await onSave(a)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    }
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onBack} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">Розподіл бюджету</h1>
      </div>

      <p className="text-sm text-neutral-500">
        Денна норма рахується лише з того, що на витрати. Решта відкладається ще до того,
        як ти побачиш «Ще сьогодні».
      </p>

      {error && <p className="text-sm text-red-600">{error}</p>}

      <div className="space-y-2">
        {data.presets.map((p) => (
          <PresetCard
            key={p.key}
            preset={p}
            budget={budget}
            currency={currency}
            active={data.active.preset === p.key}
            onPick={() => apply({ preset: p.key })}
          />
        ))}
      </div>

      <CustomSplit
        current={data.active}
        budget={budget}
        currency={currency}
        onSave={(name, buckets) => apply({ name, buckets })}
      />
    </div>
  )
}

/// Один тап = схема застосована. Ніяких «обери, потім збережи»: зайвий крок — це
/// зайве рішення, а схему все одно видно на головній одразу після вибору.
function PresetCard({ preset, budget, currency, active, onPick }: {
  preset: AllocationPreset
  budget: number | null
  currency: string
  active: boolean
  onPick: () => void
}) {
  return (
    <button
      onClick={onPick}
      aria-pressed={active}
      className={`w-full rounded-2xl p-4 text-left shadow-sm ${
        active
          ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
          : 'bg-white dark:bg-neutral-900'
      }`}
    >
      <div className="flex items-baseline justify-between gap-3">
        <span className="font-medium">{preset.name}</span>
        {active && <span className="text-xs">обрано ✓</span>}
      </div>
      <p className={`text-xs mt-0.5 ${active ? 'opacity-70' : 'text-neutral-400'}`}>{preset.hint}</p>

      <ul className={`mt-2 space-y-0.5 text-xs ${active ? 'opacity-90' : 'text-neutral-500'}`}>
        {preset.buckets.map((b, i) => (
          <li key={i} className="flex justify-between gap-3">
            <span className="truncate">{b.name}</span>
            <span className="tabular-nums shrink-0">
              {budget === null ? `${pct(b.percent)}%` : money(budget * b.percent / 100, currency)}
            </span>
          </li>
        ))}
      </ul>
    </button>
  )
}

/// Свій розподіл — за замовчуванням згорнутий. Більшості вистачить пресета, а хто хоче
/// свій, той і розгорне.
function CustomSplit({ current, budget, currency, onSave }: {
  current: { name: string; preset: string | null; buckets: AllocationBucket[] }
  budget: number | null
  currency: string
  onSave: (name: string, buckets: AllocationBucket[]) => Promise<void>
}) {
  const custom = current.preset === null
  const [name, setName] = useState(custom ? current.name : 'Свій розподіл')
  const [rows, setRows] = useState<AllocationBucket[]>(
    custom ? current.buckets : [{ name: 'На витрати', kind: 'Spending', percent: 80 },
                                { name: 'Заощадження', kind: 'Savings', percent: 20 }],
  )
  const [busy, setBusy] = useState(false)

  const total = rows.reduce((s, r) => s + (Number.isFinite(r.percent) ? r.percent : 0), 0)
  const valid = total === 100 && rows.length > 0 && rows.every((r) => r.name.trim() !== '' && r.percent > 0)

  function patch(i: number, p: Partial<AllocationBucket>) {
    setRows(rows.map((r, j) => (j === i ? { ...r, ...p } : r)))
  }

  async function save() {
    if (!valid || busy) return
    setBusy(true)
    try {
      await onSave(name, rows)
    } finally {
      setBusy(false)
    }
  }

  return (
    <details open={custom} className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm">
      <summary className="cursor-pointer list-none text-sm font-medium text-neutral-400">
        Свій розподіл {custom && '· активний'}
      </summary>

      <div className="mt-3 space-y-3">
        <input
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Назва схеми"
          className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />

        {rows.map((r, i) => (
          <div key={i} className="flex items-center gap-2">
            <input
              type="text"
              value={r.name}
              onChange={(e) => patch(i, { name: e.target.value })}
              placeholder="Назва кошика"
              className="flex-1 min-w-0 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
            />
            <select
              value={r.kind}
              onChange={(e) => patch(i, { kind: e.target.value as BucketKind })}
              className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-2 py-2 text-sm"
              aria-label="Для чого кошик"
            >
              {KINDS.map((k) => <option key={k.kind} value={k.kind}>{k.label}</option>)}
            </select>
            <input
              type="text"
              inputMode="decimal"
              value={String(r.percent)}
              onChange={(e) => patch(i, { percent: Number(e.target.value.replace(',', '.')) })}
              className="w-14 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-2 py-2 text-sm tabular-nums text-right outline-none"
              aria-label={`Частка кошика ${r.name}`}
            />
            <span className="text-neutral-400 text-sm">%</span>
            <button
              onClick={() => setRows(rows.filter((_, j) => j !== i))}
              className="text-neutral-300 hover:text-red-500 px-1"
              aria-label={`Прибрати кошик ${r.name}`}
            >
              ✕
            </button>
          </div>
        ))}

        <button
          onClick={() => setRows([...rows, { name: '', kind: 'Other', percent: 0 }])}
          className="text-sm text-neutral-500"
        >
          + Ще кошик
        </button>

        <p className={`text-sm tabular-nums ${total === 100 ? 'text-neutral-400' : 'text-amber-600'}`}>
          Разом {pct(total)}%
          {total !== 100 && ` — має бути 100% (${total > 100 ? 'зайве' : 'бракує'} ${pct(Math.abs(100 - total))}%)`}
        </p>

        {budget !== null && valid && (
          <p className="text-xs text-neutral-400">
            На витрати вийде{' '}
            {money(
              budget * rows.filter((r) => r.kind === 'Spending').reduce((s, r) => s + r.percent, 0) / 100,
              currency,
            )}
            {' '}з {money(budget, currency)}
          </p>
        )}

        <button
          onClick={save}
          disabled={!valid || busy}
          className="w-full rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-3 py-2.5 font-medium disabled:opacity-40"
        >
          Застосувати свій розподіл
        </button>
      </div>
    </details>
  )
}

export { KIND_LABEL }

/// Відсотки без хвоста нулів: 20, а не 20.00.
function pct(v: number): string {
  return String(Math.round(v * 100) / 100)
}
