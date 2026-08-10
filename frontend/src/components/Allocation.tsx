import { useState } from 'react'
import type {
  Allocation as AllocationData, AllocationBucket, AllocationPreset, BucketKind, SaveAllocation,
} from '../types'
import { money, parseAmount } from '../format'
import { CardSkeleton, FormError, Screen } from './Screen'

interface Props {
  data: AllocationData | null
  /// The period's budget, so shares can be shown as money rather than percentages. Null = not set.
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

export function Allocation({ data, budget, currency, onSave, onBack }: Props) {
  const [error, setError] = useState<string | null>(null)
  // The name of the scheme just applied, so the screen can say what changed this period.
  const [appliedName, setAppliedName] = useState<string | null>(null)

  // The header stays while loading — the way back must not depend on the data arriving.
  if (!data) {
    return (
      <Screen title="Розподіл бюджету" onBack={onBack}>
        <CardSkeleton />
      </Screen>
    )
  }

  async function apply(a: SaveAllocation, name: string) {
    setError(null)
    try {
      await onSave(a)
      setAppliedName(name)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    }
  }

  return (
    <Screen
      title="Розподіл бюджету"
      onBack={onBack}
      subtitle="Денна норма рахується лише з того, що на витрати. Кожен інший кошик — це банка: застосунок створює її під назвою кошика і сам кладе туди частку, ще до того, як ти побачиш «Можна витратити сьогодні»."
      footnote="Нова схема діє одразу на цей період — додаток перекладає гроші в банках сам. Минулі періоди не змінюються, а те, що ти вніс руками, лишається понад план. Банка з такою ж назвою, як кошик, не дублюється — вона просто продовжує наповнюватись."
    >
      <FormError>{error}</FormError>
      <AppliedNote active={data.active} appliedName={appliedName} budget={budget} currency={currency} />

      <div className="space-y-2">
        {data.presets.map((p) => (
          <PresetCard
            key={p.key}
            preset={p}
            budget={budget}
            currency={currency}
            active={data.active.preset === p.key}
            onPick={() => apply({ preset: p.key }, p.name)}
          />
        ))}
      </div>

      <CustomSplit
        current={data.active}
        budget={budget}
        currency={currency}
        onSave={(name, buckets) => apply({ name, buckets }, name)}
      />
    </Screen>
  )
}

/// What changing the scheme actually did. "А гроші, які вже відкладені?" comes up the moment
/// the tap lands, so the answer belongs on this same screen: this period was recomputed, past
/// ones were not. Shown only once the server has confirmed the new scheme — otherwise these
/// would be the old one's figures.
///
/// Every non-spending bucket is named, with the money going into it, because that bucket IS a
/// jar: the app creates one per bucket by name and pours the share in by itself. Without the
/// names, picking a scheme with a pension bucket silently produced a jar the user never asked
/// for and only met later on the savings screen — the single most confusing thing this screen
/// did. A bucket whose name a jar already carries keeps that jar, balance and all.
function AppliedNote({ active, appliedName, budget, currency }: {
  active: { name: string; buckets: AllocationBucket[] }
  appliedName: string | null
  budget: number | null
  currency: string
}) {
  if (appliedName === null || active.name !== appliedName) return null

  const shown = (percent: number) =>
    budget === null ? `${pct(percent)}%` : money(budget * percent / 100, currency)

  const spending = active.buckets
    .filter((b) => b.kind === 'Spending')
    .reduce((s, b) => s + b.percent, 0)
  const jars = active.buckets.filter((b) => b.kind !== 'Spending')

  return (
    <div className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 text-xs text-neutral-500 space-y-1">
      <p>Застосовано. Цей період перерахували: на витрати {shown(spending)}.</p>
      {jars.length > 0 && (
        <>
          <p>Решта йде в банки — застосунок наповнить їх сам, руками нічого робити не треба:</p>
          <ul className="tabular-nums">
            {jars.map((b, i) => (
              <li key={i}>· «{b.name}» {shown(b.percent)} щоперіоду</li>
            ))}
          </ul>
        </>
      )}
      <p>Минулі періоди лишились як були.</p>
    </div>
  )
}

/// One tap applies the scheme. No "pick, then save": the extra step is an extra decision, and
/// the scheme shows up on the home screen right after the choice anyway.
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
            {/* Says which rows turn into jars before the tap, not after it. */}
            <span className="truncate">
              {b.name}
              {b.kind !== 'Spending' && <span className="opacity-60"> · банка</span>}
            </span>
            <span className="tabular-nums shrink-0">
              {budget === null ? `${pct(b.percent)}%` : money(budget * b.percent / 100, currency)}
            </span>
          </li>
        ))}
      </ul>
    </button>
  )
}

/// A split of one's own, collapsed by default. A preset is enough for most people, and
/// whoever wants their own will open it.
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
  // Percentages stay the source of truth — the budget is a different number every period, and a
  // scheme pinned to złoty would quietly stop adding up the month the income changed. Money is
  // a way of TYPING one: "1500 zł" is easier to decide than "25%", and the two are the same
  // answer as long as there is a budget to convert against.
  const [inMoney, setInMoney] = useState(false)
  const byMoney = inMoney && budget !== null && budget > 0

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

        {budget !== null && budget > 0 && (
          <div className="flex gap-2">
            {[false, true].map((m) => (
              <button
                key={String(m)}
                onClick={() => setInMoney(m)}
                aria-pressed={inMoney === m}
                className={`flex-1 rounded-xl px-3 py-1.5 text-xs ${
                  inMoney === m
                    ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                    : 'bg-neutral-100 dark:bg-neutral-800 text-neutral-500'
                }`}
              >
                {m ? `Сумою, ${currency}` : 'Відсотком'}
              </button>
            ))}
          </div>
        )}

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
              value={byMoney ? asMoney(r.percent, budget!) : String(r.percent)}
              // Cleared field is 0, not "not a number": the value is written straight into the
              // bucket and rendered back, so a NaN here would appear in the box as "NaN".
              onChange={(e) => patch(i, {
                percent: byMoney
                  ? asPercent(parseAmount(e.target.value) || 0, budget!)
                  : parseAmount(e.target.value) || 0,
              })}
              className={`rounded-xl bg-neutral-100 dark:bg-neutral-800 px-2 py-2 text-sm tabular-nums text-right outline-none ${
                byMoney ? 'w-20' : 'w-14'
              }`}
              aria-label={`Частка кошика ${r.name}`}
            />
            <span className="text-neutral-400 text-sm">{byMoney ? currency : '%'}</span>
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
          Разом {byMoney ? money(budget! * total / 100, currency) : `${pct(total)}%`}
          {total !== 100 && (
            byMoney
              ? ` — має бути ${money(budget!, currency)} (${total > 100 ? 'зайве' : 'бракує'} ${money(Math.abs(budget! * (100 - total) / 100), currency)})`
              : ` — має бути 100% (${total > 100 ? 'зайве' : 'бракує'} ${pct(Math.abs(100 - total))}%)`
          )}
        </p>

        {/* The honest half of typing in money: the figure holds only while the budget does. */}
        {byMoney && (
          <p className="text-xs text-neutral-400">
            Зберігається частка, не сума: {money(budget!, currency)} цього періоду — це 100%.
            Наступного періоду з іншим доходом ті самі відсотки дадуть інші суми.
          </p>
        )}

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

/// A share of the budget as money, and back. Rounded to whole złoty on the way out — a bucket
/// shown as "1499,99 zł" invites correcting a rounding artefact — and to two decimals on the
/// way in, which is as fine as a percentage of a monthly budget can meaningfully be.
function asMoney(percent: number, budget: number): string {
  return String(Math.round(budget * percent / 100))
}

function asPercent(amount: number, budget: number): number {
  return Math.round((amount / budget) * 10000) / 100
}

/// Percentages without trailing zeros: 20, not 20.00.
function pct(v: number): string {
  return String(Math.round(v * 100) / 100)
}
