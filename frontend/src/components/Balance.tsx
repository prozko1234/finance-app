import { useState } from 'react'
import type { OpeningBalance, SaveOpeningBalance } from '../types'
import { CURRENCIES, todayIso } from '../types'
import { dayMonth, money, parseAmount } from '../format'
import { Card, CardSkeleton, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

interface Props {
  data: OpeningBalance | null
  /// The reading currency — the default for a new count.
  currency: string
  onSet: (b: SaveOpeningBalance) => Promise<void>
  onClear: () => Promise<void>
  onBack: () => void
}

/// "Скільки в мене зараз є" — the same figure as on the banking app's front screen.
///
/// The mechanism existed from the start, but it could ONLY be entered during onboarding, that
/// is once in the app's lifetime. Yet it overrides the budget from income, shifts the counting
/// window and stands the savings plan down until the next payday — it drives the headline
/// figure. A wrong amount could not be corrected: it held until the period ended. It is an
/// ordinary screen now: count again, see what is in force, and clear it.
export function Balance({ data, currency, onSet, onClear, onBack }: Props) {
  return (
    <Screen
      title="Скільки в мене зараз"
      onBack={onBack}
      subtitle="Коли на рахунку вже не те, що прийшло — порахуй залишок, і денна норма піде від нього."
      footnote="Це не витрата й не дохід: застосунок просто вірить тобі, що зараз є стільки. Наступного періоду бюджет знову рахується з доходу."
    >
      {data === null ? <CardSkeleton /> : (
        <>
          <Current data={data} onClear={onClear} />
          <CountForm currency={currency} onSet={onSet} />
        </>
      )}
    </Screen>
  )
}

/// What is in force right now. The API returned `appliesNow` long ago but nothing showed it,
/// so there was nowhere to find out why the norm was smaller than expected.
function Current({ data, onClear }: { data: OpeningBalance; onClear: () => Promise<void> }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!data.isSet || data.amount === null) {
    return (
      <Card>
        <SectionTitle>Зараз не задано</SectionTitle>
        <p className="text-sm text-neutral-500">
          Бюджет рахується з доходу за період — так і має бути, поки все прийшло цього періоду.
        </p>
      </Card>
    )
  }

  async function clear() {
    setBusy(true)
    setError(null)
    try {
      await onClear()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося прибрати')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>{data.appliesNow ? 'Діє зараз' : 'Уже не діє'}</SectionTitle>
      <p className="text-3xl font-bold tabular-nums">{money(data.amount, data.currency)}</p>
      <p className="text-sm text-neutral-500">
        {data.appliesNow
          ? `Порахував ${data.date ? dayMonth(data.date) : '—'}. Денна норма йде від цієї суми, а витрати до того дня вже в ній. Відкладати цей період застосунок не буде — ці гроші на життя.`
          : `Порахував ${data.date ? dayMonth(data.date) : '—'} — це минулий період, тож зараз бюджет знову з доходу.`}
      </p>

      <FormError>{error}</FormError>

      {data.appliesNow && (
        <button
          onClick={clear}
          disabled={busy}
          className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 py-2.5 font-medium disabled:opacity-40"
        >
          {busy ? 'Прибираю…' : 'Прибрати — рахувати з доходу'}
        </button>
      )}
    </Card>
  )
}

/// A new count replaces the previous one: a history of guesses about the same period would
/// only raise the question of which one is in force.
function CountForm({ currency, onSet }: {
  currency: string
  onSet: (b: SaveOpeningBalance) => Promise<void>
}) {
  const [amount, setAmount] = useState('')
  const [entryCurrency, setEntryCurrency] = useState(currency)
  const [date, setDate] = useState(todayIso())
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const value = parseAmount(amount)
  const valid = value > 0

  async function save() {
    if (!valid || busy) return
    setBusy(true)
    setError(null)
    try {
      await onSet({ amount: value, currency: entryCurrency, date })
      setAmount('')
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Порахувати заново</SectionTitle>

      <div className="flex gap-2">
        <input
          type="text"
          inputMode="decimal"
          placeholder="0"
          value={amount}
          onChange={(e) => { setAmount(e.target.value); setSaved(false) }}
          aria-label="Сума на руках"
          className="flex-1 text-3xl font-bold tabular-nums bg-transparent outline-none w-full"
        />
        <select
          value={entryCurrency}
          onChange={(e) => setEntryCurrency(e.target.value)}
          aria-label="Валюта"
          className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 font-medium"
        >
          {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>

      {/* The day of the count, because spending is measured from it. Yesterday's figure is an
          honest one too, if yesterday is when you looked. */}
      <div>
        <label className="text-xs text-neutral-400">Коли дивився</label>
        <input
          type="date"
          value={date}
          max={todayIso()}
          onChange={(e) => setDate(e.target.value)}
          className="mt-1 w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />
      </div>

      <FormError>{error}</FormError>

      <PrimaryButton onClick={save} disabled={!valid || busy} saved={saved}>
        Це в мене зараз
      </PrimaryButton>
    </Card>
  )
}
