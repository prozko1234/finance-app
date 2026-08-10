import { useEffect, useState } from 'react'
import type { TaxActuals } from '../types'
import { money, parseAmount } from '../format'
import { useSaveTaxActuals, useTaxActuals } from '../hooks'

/// «Що сказала книгова» — the month's real contributions, typed over the engine's.
///
/// The engine works ZUS, the health contribution and PIT out of a profile, and it is right
/// often enough to be worth having. It is still a model: the figure that actually gets paid
/// comes from a person with the full picture — a month with a sick note, a deduction the app
/// knows nothing about, a rate that changed before the code did. Until now the only way to
/// reconcile the two was to stop believing the app.
///
/// Collapsed by default. Most months there is nothing to correct, and a form that is always
/// open on the income screen is a form that gets scrolled past.
///
/// The figures are MONTHLY, not per invoice — Polish contributions are — so this is the month
/// being written in, whichever invoice put you here. The heading says so, because a field
/// under an invoice reads as belonging to it.
export function TaxActualsBlock() {
  const { data } = useTaxActuals()
  const [open, setOpen] = useState(false)

  if (!data) return null

  return (
    <details
      open={open}
      onToggle={(e) => setOpen((e.currentTarget as HTMLDetailsElement).open)}
      className="rounded-xl bg-neutral-50 dark:bg-neutral-800/60 px-3 py-2"
    >
      <summary className="cursor-pointer list-none text-xs text-neutral-500">
        Податки за місяць
        <span className="text-neutral-400">
          {' · '}
          {overridden(data) ? 'вписані руками' : 'рахує застосунок'}
        </span>
      </summary>

      <Form data={data} />
    </details>
  )
}

function overridden(data: TaxActuals): boolean {
  return data.zusSocial !== null || data.health !== null || data.pit !== null
}

function Form({ data }: { data: TaxActuals }) {
  const save = useSaveTaxActuals()
  const [zus, setZus] = useState(text(data.zusSocial))
  const [health, setHealth] = useState(text(data.health))
  const [pit, setPit] = useState(text(data.pit))
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  // The month rolls over, and another device may have saved figures for it. Without this the
  // boxes would keep showing whatever was in them when the form first rendered.
  useEffect(() => {
    setZus(text(data.zusSocial))
    setHealth(text(data.health))
    setPit(text(data.pit))
  }, [data.month, data.zusSocial, data.health, data.pit])

  async function submit() {
    setError(null)
    try {
      await save.mutateAsync({
        month: data.month,
        zusSocial: value(zus),
        health: value(health),
        pit: value(pit),
      })
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    }
  }

  return (
    <div className="mt-2 space-y-2">
      <p className="text-xs text-neutral-400 leading-relaxed">
        Це суми за весь місяць, а не за цю фактуру — внески в Польщі місячні. Порожнє поле
        означає «рахуй сам», і застосунок підставить свою цифру.
      </p>

      <Row label="ZUS społeczne" computed={data.computedZusSocial} currency={data.currency}
        value={zus} onChange={(v) => { setZus(v); setSaved(false) }} />
      <Row label="Zdrowotna" computed={data.computedHealth} currency={data.currency}
        value={health} onChange={(v) => { setHealth(v); setSaved(false) }} />
      <Row label="Податок (PIT)" computed={data.computedPit} currency={data.currency}
        value={pit} onChange={(v) => { setPit(v); setSaved(false) }} />

      {error && <p className="text-xs text-red-600">{error}</p>}

      <button
        onClick={submit}
        disabled={save.isPending}
        className="w-full rounded-lg bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-3 py-1.5 text-xs font-medium disabled:opacity-40"
      >
        {save.isPending ? 'Зберігаю…' : saved ? 'Збережено ✓' : 'Зберегти податки місяця'}
      </button>
    </div>
  )
}

/// The engine's figure is the placeholder, not the value: a box pre-filled with it would make
/// every month look hand-checked, and there would be no way to tell a corrected figure from one
/// that was simply never touched.
function Row({ label, computed, currency, value, onChange }: {
  label: string
  computed: number
  currency: string
  value: string
  onChange: (v: string) => void
}) {
  return (
    <div className="flex items-center gap-2 text-xs">
      <span className="flex-1 min-w-0 text-neutral-500 truncate">{label}</span>
      <input
        type="text"
        inputMode="decimal"
        placeholder={money(computed, currency).replace(/\s*[^\d,.\s]+$/, '').trim()}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        aria-label={label}
        className="w-24 rounded-lg bg-white dark:bg-neutral-900 px-2 py-1.5 text-right tabular-nums outline-none"
      />
      <span className="text-neutral-400 w-6">{currency === 'PLN' ? 'zł' : currency}</span>
    </div>
  )
}

function text(v: number | null): string {
  return v === null ? '' : String(v)
}

/// An empty box means "the engine's figure stands", which is a real answer and not a zero.
function value(input: string): number | null {
  return input.trim() === '' ? null : parseAmount(input)
}
