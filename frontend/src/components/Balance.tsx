import { useState } from 'react'
import type { MonthlyNeed, OpeningBalance, SafeToSpend, SaveOpeningBalance } from '../types'
import { CURRENCIES, todayIso } from '../types'
import { dayMonth, money, parseAmount } from '../format'
import { Card, CardSkeleton, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

interface Props {
  data: OpeningBalance | null
  /// Where the app thinks the money is. Null while it loads — the screen keeps its header.
  summary: SafeToSpend | null
  /// What the month will ask for. Null while it loads; the card simply waits.
  need: MonthlyNeed | null
  /// The reading currency — the default for a new count.
  currency: string
  onSet: (b: SaveOpeningBalance) => Promise<void>
  onClear: () => Promise<void>
  /// The gap between the app and the bank, written down as what it actually was.
  onRecordGap: (kind: 'expense' | 'income', amount: number) => Promise<void>
  onBack: () => void
}

/// «Мої гроші» — what I have, what the month will ask for, and what the bank says.
///
/// This was "Скільки в мене зараз": one screen about one hidden mechanism, whose top half was
/// a figure from a period that had ended. The mechanism is still here — counting what you hold
/// overrides the budget from income — but it is the last of three answers rather than the whole
/// screen, and it is reached by explaining a difference rather than by declaring one.
export function Balance({ data, summary, need, currency, onSet, onClear, onRecordGap, onBack }: Props) {
  return (
    <Screen
      title="Мої гроші"
      onBack={onBack}
      subtitle="Скільки в мене є, скільки місяць попросить, і що робити, коли банк каже інше."
      footnote="Звірка нічого не забирає й нічого не додає — вона лише пояснює різницю. Гроші, що лежали на рахунку ще до того, як застосунок про них дізнався, у розрахунок не входять."
    >
      {summary === null ? <CardSkeleton /> : <WhatIHave summary={summary} />}
      <MonthlyNeedCard need={need} />
      {data === null ? <CardSkeleton /> : (
        <>
          <Reconcile
            summary={summary}
            currency={currency}
            onSet={onSet}
            onRecordGap={onRecordGap}
          />
          <CountInForce data={data} onClear={onClear} />
        </>
      )}
    </Screen>
  )
}

/// The three piles the money is actually in. Before this the app could tell you what was safe
/// to spend today and what was in the jars, but never both at once — so "скільки в мене
/// взагалі" was a question you answered by opening two screens and adding.
function WhatIHave({ summary }: { summary: SafeToSpend }) {
  const c = summary.currency
  const jars = summary.envelopes.reduce((s, e) => s + e.balance, 0)
  const held = summary.reservedRecurring + summary.reservedDebts

  return (
    <Card>
      <SectionTitle>Скільки в мене</SectionTitle>
      <p className="text-3xl font-bold tabular-nums">{money(expected(summary), c)}</p>

      <dl className="space-y-1.5 text-sm">
        <Line label="На витрати" hint={`до ${dayMonth(summary.periodEnd)}`}>
          {money(summary.remainingThisPeriod ?? 0, c)}
        </Line>
        <Line label="У банках" hint="відкладене">{money(jars, c)}</Line>
        {held > 0 && (
          <Line label="Притримано" hint="підписки й борги">{money(held, c)}</Line>
        )}
      </dl>
    </Card>
  )
}

/// What the app believes is sitting in the account: what is still free to spend, plus
/// everything it is holding back, plus what has already been put in jars. All three are money
/// that has not left — the difference between them is only what the app is willing to promise.
function expected(summary: SafeToSpend): number {
  const jars = summary.envelopes.reduce((s, e) => s + e.balance, 0)
  return (summary.remainingThisPeriod ?? 0) + jars + summary.reservedRecurring + summary.reservedDebts
}

function Line({ label, hint, children }: {
  label: string; hint?: string; children: React.ReactNode
}) {
  return (
    <div className="flex justify-between gap-3">
      <dt className="text-neutral-500">
        {label}
        {hint && <span className="text-neutral-400 text-xs"> · {hint}</span>}
      </dt>
      <dd className="tabular-nums shrink-0">{children}</dd>
    </div>
  )
}

/// The other half of the same question. A balance that looks healthy against a month that costs
/// more is the number people get wrong, and until now the app never said the second part out
/// loud — the standing charges lived on one screen, the jar plan on another, and nobody added
/// them up.
///
/// Only the last line is a guess, and it says so rather than quietly rounding the total.
function MonthlyNeedCard({ need }: { need: MonthlyNeed | null }) {
  if (!need) return <CardSkeleton />

  const c = need.currency

  return (
    <Card>
      <SectionTitle>Треба на місяць</SectionTitle>
      <p className="text-3xl font-bold tabular-nums">{money(need.total, c)}</p>

      <dl className="space-y-1.5 text-sm">
        <Line label="Підписки й регулярне">{money(need.recurring, c)}</Line>
        <Line label="Відкладати за планом">{money(need.jars, c)}</Line>
        {need.debts > 0 && <Line label="Борги">{money(need.debts, c)}</Line>}
        <Line label="Звичні витрати" hint={need.typicalKnown ? 'медіана 3 міс.' : undefined}>
          {need.typicalKnown ? money(need.typical ?? 0, c) : '—'}
        </Line>
      </dl>

      {!need.typicalKnown && (
        <p className="text-xs text-neutral-400">
          Звичних витрат ще не видно — треба два повні місяці, щоб було з чого брати медіану.
          Поки що в сумі лише те, що точно відомо.
        </p>
      )}
    </Card>
  )
}

/// The answer to "чому в банку інша цифра". The app's own figure is broken out above it, so a
/// gap can be traced to the line that is wrong instead of being taken on faith — and the three
/// answers are the only three things a gap can mean.
///
/// "Просто вирівняй" is the old opening balance, which is why it warns: it does not explain the
/// difference, it declares the account's figure to be the truth and rebuilds the daily norm
/// from it. The other two leave a transaction behind, which is undoable and shows up in the
/// history where it belongs.
function Reconcile({ summary, currency, onSet, onRecordGap }: {
  summary: SafeToSpend | null
  currency: string
  onSet: (b: SaveOpeningBalance) => Promise<void>
  onRecordGap: (kind: 'expense' | 'income', amount: number) => Promise<void>
}) {
  const [amount, setAmount] = useState('')
  const [entryCurrency, setEntryCurrency] = useState(currency)
  const [date, setDate] = useState(todayIso())
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const real = parseAmount(amount)
  const valid = real > 0
  // A gap is only meaningful against a figure in the same currency — comparing a hryvnia count
  // with a złoty total would produce a difference of several thousand and mean nothing.
  const comparable = valid && summary !== null && entryCurrency === summary.currency
  const gap = comparable ? Math.round((real - expected(summary)) * 100) / 100 : 0

  async function run(action: () => Promise<void>) {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await action()
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
      <SectionTitle>Звірка з банком</SectionTitle>
      <p className="text-sm text-neutral-500">
        Скільки насправді на рахунку? Порівняю зі своїм розрахунком і покажу, де розійшлось.
      </p>

      <div className="flex gap-2">
        <input
          type="text"
          inputMode="decimal"
          placeholder="0"
          value={amount}
          onChange={(e) => { setAmount(e.target.value); setSaved(false) }}
          aria-label="Скільки насправді на рахунку"
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

      {comparable && Math.abs(gap) >= 0.01 && (
        <div className="space-y-2">
          <p className="text-sm">
            <span className="text-neutral-500">Різниця </span>
            <span className={`font-semibold tabular-nums ${gap < 0 ? 'text-red-600' : 'text-emerald-600'}`}>
              {gap > 0 ? '+' : '−'}{money(Math.abs(gap), entryCurrency)}
            </span>
            <span className="text-neutral-500">
              {gap < 0 ? ' — у банку менше, ніж я думав' : ' — у банку більше, ніж я думав'}
            </span>
          </p>

          <button
            onClick={() => run(() => onRecordGap(gap < 0 ? 'expense' : 'income', Math.abs(gap)))}
            disabled={busy}
            className="w-full rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 py-2.5 font-medium disabled:opacity-40"
          >
            {gap < 0 ? 'Була витрата, яку не записав' : 'Був дохід, який не записав'}
          </button>
        </div>
      )}

      {comparable && Math.abs(gap) < 0.01 && valid && (
        <p className="text-sm text-emerald-600">Збігається — рахувати нема чого.</p>
      )}

      {valid && !comparable && summary && (
        <p className="text-xs text-neutral-400">
          Розрахунок ведеться в {summary.currency}, тож різницю в {entryCurrency} я не покажу —
          але вирівняти можна.
        </p>
      )}

      <PrimaryButton
        onClick={() => run(() => onSet({ amount: real, currency: entryCurrency, date }))}
        disabled={!valid || busy}
        saved={saved}
      >
        Просто вирівняй — це в мене зараз
      </PrimaryButton>
      <p className="text-xs text-neutral-400">
        Вирівнювання не пояснює різниці: воно каже застосунку вважати цю суму правдою, і денна
        норма піде від неї до кінця періоду.
      </p>
    </Card>
  )
}

/// Which count is in force, if any. A count from a period that has ended changes nothing today,
/// and it used to open this screen as a card with the figure at 3xl — the first thing read was a
/// number that cannot be acted on. It stays, because "чому норма така" is sometimes answered by
/// "бо ти рахував 3-го", but at the weight of a footnote.
function CountInForce({ data, onClear }: { data: OpeningBalance; onClear: () => Promise<void> }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!data.isSet || data.amount === null) return null

  if (!data.appliesNow) {
    return (
      <p className="text-sm text-neutral-500">
        Востаннє вирівнював {data.date ? dayMonth(data.date) : '—'}
        {' '}({money(data.amount, data.currency)}) — це минулий період, тож зараз бюджет знову
        з доходу.
      </p>
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
      <SectionTitle>Діє зараз</SectionTitle>
      <p className="text-3xl font-bold tabular-nums">{money(data.amount, data.currency)}</p>
      <p className="text-sm text-neutral-500">
        Вирівняв {data.date ? dayMonth(data.date) : '—'}. Денна норма йде від цієї суми, а
        витрати до того дня вже в ній. Відкладати цей період застосунок не буде — ці гроші
        на життя.
      </p>

      <FormError>{error}</FormError>

      <button
        onClick={clear}
        disabled={busy}
        className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 py-2.5 font-medium disabled:opacity-40"
      >
        {busy ? 'Прибираю…' : 'Прибрати — рахувати з доходу'}
      </button>
    </Card>
  )
}
