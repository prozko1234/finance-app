import { useState } from 'react'
import type {
  Debt, DebtDirection, DebtPaymentSource, Debts as DebtsData, EnvelopeSummary, SaveDebt, SaveDebtPayment,
} from '../types'
import { BASE_CURRENCY, CURRENCIES, todayIso } from '../types'
import { dayMonth, money } from '../format'
import { Card, CardSkeleton, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

interface Props {
  data: DebtsData | null
  /// The jars a repayment can be taken out of. Passed in rather than fetched here: the same
  /// list is already on screen elsewhere, and two fetches of it could disagree.
  envelopes: EnvelopeSummary[]
  onCreate: (d: SaveDebt) => Promise<void>
  onDelete: (id: number) => Promise<void>
  onSetClosed: (id: number, closed: boolean) => Promise<void>
  onPay: (id: number, p: SaveDebtPayment) => Promise<void>
  onBack: () => void
}

/// Debts, both ways round.
///
/// They used to be a jar with a different label on the button: money went in, the balance grew,
/// and «Погасити» made the number bigger. For a debt that reads backwards, which is why it
/// "дивно працює" — so this screen leads with what is still OWED, and that figure goes down.
export function Debts({ data, envelopes, onCreate, onDelete, onSetClosed, onPay, onBack }: Props) {
  const [adding, setAdding] = useState<DebtDirection | null>(null)

  if (!data) {
    return (
      <Screen title="Борги" onBack={onBack}>
        <CardSkeleton />
      </Screen>
    )
  }

  return (
    <Screen
      title="Борги"
      onBack={onBack}
      footnote="Повернені тобі гроші — не дохід: вони не проходять через податки, просто повертаються у бюджет."
    >
      <ReserveNote amount={data.reservedThisPeriod} currency={data.currency} />

      <Side
        title="Я винен"
        empty="Нікому не винен."
        total={data.iOweTotal}
        debts={data.iOwe}
        currency={data.currency}
        envelopes={envelopes}
        adding={adding === 'IOwe'}
        onAdd={() => setAdding(adding === 'IOwe' ? null : 'IOwe')}
        onCreate={onCreate}
        onDelete={onDelete}
        onSetClosed={onSetClosed}
        onPay={onPay}
        direction="IOwe"
      />

      <Side
        title="Мені винні"
        empty="Ніхто не винен."
        total={data.theyOweMeTotal}
        debts={data.theyOweMe}
        currency={data.currency}
        envelopes={envelopes}
        adding={adding === 'TheyOweMe'}
        onAdd={() => setAdding(adding === 'TheyOweMe' ? null : 'TheyOweMe')}
        onCreate={onCreate}
        onDelete={onDelete}
        onSetClosed={onSetClosed}
        onPay={onPay}
        direction="TheyOweMe"
      />
    </Screen>
  )
}

/// Money missing from the daily norm with nothing on screen explaining it is the complaint
/// this whole area answers, so the reserve says its own name out loud.
function ReserveNote({ amount, currency }: { amount: number; currency: string }) {
  if (amount <= 0) return null

  return (
    <p className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 text-xs text-neutral-500">
      Цього періоду {money(amount, currency)} тримається на борги — цих грошей нема
      в «Можна витратити сьогодні».
    </p>
  )
}

function Side({
  title, empty, total, debts, currency, envelopes, direction,
  adding, onAdd, onCreate, onDelete, onSetClosed, onPay,
}: {
  title: string
  empty: string
  total: number
  debts: Debt[]
  currency: string
  envelopes: EnvelopeSummary[]
  direction: DebtDirection
  adding: boolean
  onAdd: () => void
  onCreate: (d: SaveDebt) => Promise<void>
  onDelete: (id: number) => Promise<void>
  onSetClosed: (id: number, closed: boolean) => Promise<void>
  onPay: (id: number, p: SaveDebtPayment) => Promise<void>
}) {
  const open = debts.filter((d) => d.closedOn === null)
  const settled = debts.filter((d) => d.closedOn !== null)

  return (
    <div className="space-y-2">
      <div className="flex items-baseline justify-between gap-3">
        <SectionTitle>{title}</SectionTitle>
        <span className="text-lg font-semibold tabular-nums">{money(total, currency)}</span>
      </div>

      {open.length === 0 && <p className="text-sm text-neutral-400">{empty}</p>}

      {open.map((d) => (
        <DebtCard
          key={d.id}
          debt={d}
          currency={currency}
          envelopes={envelopes}
          onDelete={onDelete}
          onSetClosed={onSetClosed}
          onPay={onPay}
        />
      ))}

      {settled.length > 0 && <Settled debts={settled} currency={currency} onSetClosed={onSetClosed} />}

      <button
        onClick={onAdd}
        className="w-full rounded-xl border border-dashed border-neutral-300 dark:border-neutral-700 py-2.5 text-sm text-neutral-500"
      >
        {adding ? 'Не додавати' : '+ Додати'}
      </button>

      {adding && <NewDebt direction={direction} onCreate={onCreate} onDone={onAdd} />}
    </div>
  )
}

function DebtCard({ debt, currency, envelopes, onDelete, onSetClosed, onPay }: {
  debt: Debt
  currency: string
  envelopes: EnvelopeSummary[]
  onDelete: (id: number) => Promise<void>
  onSetClosed: (id: number, closed: boolean) => Promise<void>
  onPay: (id: number, p: SaveDebtPayment) => Promise<void>
}) {
  const [paying, setPaying] = useState(false)
  const incoming = debt.direction === 'TheyOweMe'

  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-medium truncate">{debt.person}</p>
          <p className="text-xs text-neutral-400">
            {/* The original sum is kept beside the remainder: without it "лишилось 400"
                says nothing about whether that is nearly done or barely started. */}
            з {money(debt.amount, currency)}
            {debt.deadline && <> · до {dayMonth(debt.deadline)}</>}
          </p>
        </div>
        <p className="text-xl font-bold tabular-nums shrink-0">{money(debt.outstanding, currency)}</p>
      </div>

      {debt.overdue && (
        <p className="text-xs text-red-600">Дедлайн минув, а гроші ще не всі.</p>
      )}

      {debt.perPeriod > 0 && (
        <p className="text-xs text-neutral-500">
          Цього періоду відкладається {money(debt.perPeriod, currency)}
          {debt.periodsLeft > 0 && <> · періодів лишилось: {debt.periodsLeft}</>}
        </p>
      )}

      {debt.note && <p className="text-xs text-neutral-500">{debt.note}</p>}

      {debt.payments.length > 0 && (
        <ul className="space-y-1">
          {debt.payments.map((p) => (
            <li key={p.id} className="flex justify-between gap-3 text-xs text-neutral-500">
              <span className="truncate">
                {dayMonth(p.date)}
                {p.source === 'Envelope' && p.envelopeName && <> · з «{p.envelopeName}»</>}
                {p.source === 'AlreadyHappened' && <> · було раніше</>}
              </span>
              <span className="tabular-nums shrink-0">{money(p.amount, currency)}</span>
            </li>
          ))}
        </ul>
      )}

      <div className="flex flex-wrap gap-2 pt-1">
        <button
          onClick={() => setPaying(!paying)}
          className="rounded-lg bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-3 py-1.5 text-sm"
        >
          {paying ? 'Не зараз' : incoming ? 'Повернули' : 'Погасив'}
        </button>
        <button
          onClick={() => onSetClosed(debt.id, true)}
          className="rounded-lg px-3 py-1.5 text-sm text-neutral-500"
        >
          Закрити
        </button>
        <button
          onClick={() => onDelete(debt.id)}
          className="rounded-lg px-3 py-1.5 text-sm text-neutral-400"
        >
          Видалити
        </button>
      </div>

      {paying && (
        <PaymentForm
          debt={debt}
          envelopes={envelopes}
          onPay={onPay}
          onDone={() => setPaying(false)}
        />
      )}
    </Card>
  )
}

/// Closed debts stay readable but out of the way: they are history, and history that takes up
/// the top of the screen makes the list about the past instead of about what is still owed.
function Settled({ debts, currency, onSetClosed }: {
  debts: Debt[]
  currency: string
  onSetClosed: (id: number, closed: boolean) => Promise<void>
}) {
  const [open, setOpen] = useState(false)

  return (
    <div className="space-y-1">
      <button onClick={() => setOpen(!open)} className="text-xs text-neutral-400 underline">
        Закриті: {debts.length}
      </button>
      {open && debts.map((d) => (
        <div key={d.id} className="flex items-center justify-between gap-3 px-1 text-sm text-neutral-500">
          <span className="truncate">{d.person} · {money(d.amount, currency)}</span>
          <button onClick={() => onSetClosed(d.id, false)} className="text-xs underline shrink-0">
            Повернути в список
          </button>
        </div>
      ))}
    </div>
  )
}

/// The source is the whole point of the form, so it is asked with words rather than a switch:
/// each option is a different thing happening to the daily norm, and the user is the only one
/// who knows which happened.
function PaymentForm({ debt, envelopes, onPay, onDone }: {
  debt: Debt
  envelopes: EnvelopeSummary[]
  onPay: (id: number, p: SaveDebtPayment) => Promise<void>
  onDone: () => void
}) {
  const incoming = debt.direction === 'TheyOweMe'
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState(BASE_CURRENCY)
  const [date, setDate] = useState(todayIso())
  const [source, setSource] = useState<DebtPaymentSource>('Spendable')
  const [envelopeId, setEnvelopeId] = useState<number | null>(envelopes[0]?.id ?? null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  // Money coming back is arriving, not leaving: there is no jar to take it out of.
  const sources: { value: DebtPaymentSource; label: string }[] = incoming
    ? [
      { value: 'Spendable', label: 'Повернули зараз' },
      { value: 'AlreadyHappened', label: 'Повернули раніше' },
    ]
    : [
      { value: 'Spendable', label: 'З поточних грошей' },
      { value: 'Envelope', label: 'З банки' },
      { value: 'AlreadyHappened', label: 'Віддав раніше' },
    ]

  async function save() {
    const value = Number(amount.replace(',', '.'))
    if (!Number.isFinite(value) || value <= 0) {
      setError('Сума має бути більшою за нуль.')
      return
    }

    setBusy(true)
    setError('')
    try {
      await onPay(debt.id, {
        amount: value,
        currency,
        date,
        source,
        envelopeId: source === 'Envelope' ? envelopeId : null,
        note: null,
      })
      onDone()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вийшло зберегти.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="space-y-3 border-t border-neutral-100 dark:border-neutral-800 pt-3">
      <div className="flex gap-2">
        <input
          inputMode="decimal"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          placeholder="Скільки"
          aria-label="Сума"
          className="flex-1 min-w-0 rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2"
        />
        <select
          value={currency}
          onChange={(e) => setCurrency(e.target.value)}
          aria-label="Валюта"
          className="rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-2 py-2"
        >
          {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>

      <input
        type="date"
        value={date}
        onChange={(e) => setDate(e.target.value)}
        aria-label="Дата"
        className="w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2"
      />

      <div className="flex flex-wrap gap-2">
        {sources.map((s) => (
          <button
            key={s.value}
            onClick={() => setSource(s.value)}
            aria-pressed={source === s.value}
            className={`rounded-lg px-3 py-1.5 text-sm ${
              source === s.value
                ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                : 'bg-neutral-100 dark:bg-neutral-800 text-neutral-500'
            }`}
          >
            {s.label}
          </button>
        ))}
      </div>

      {source === 'Envelope' && (
        <select
          value={envelopeId ?? ''}
          onChange={(e) => setEnvelopeId(Number(e.target.value))}
          aria-label="Банка"
          className="w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2"
        >
          {envelopes.map((e) => <option key={e.id} value={e.id}>{e.name}</option>)}
        </select>
      )}

      <p className="text-xs text-neutral-400">
        {source === 'Spendable' && (incoming
          ? 'Гроші повертаються в бюджет цього періоду.'
          : 'Денна норма впаде на цю суму — гроші пішли зараз.')}
        {source === 'Envelope' && 'Норма не впаде: ці гроші вже були відкладені.'}
        {source === 'AlreadyHappened' && 'Цей період за них не платить — рух був раніше.'}
      </p>

      <FormError>{error}</FormError>
      <PrimaryButton onClick={save} disabled={busy}>Записати</PrimaryButton>
    </div>
  )
}

function NewDebt({ direction, onCreate, onDone }: {
  direction: DebtDirection
  onCreate: (d: SaveDebt) => Promise<void>
  onDone: () => void
}) {
  const [person, setPerson] = useState('')
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState(BASE_CURRENCY)
  const [date, setDate] = useState(todayIso())
  const [deadline, setDeadline] = useState('')
  const [reserve, setReserve] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function save() {
    const value = Number(amount.replace(',', '.'))
    if (person.trim() === '') {
      setError('Скажи, з ким цей борг.')
      return
    }
    if (!Number.isFinite(value) || value <= 0) {
      setError('Сума має бути більшою за нуль.')
      return
    }

    setBusy(true)
    setError('')
    try {
      await onCreate({
        direction,
        person: person.trim(),
        amount: value,
        currency,
        date,
        deadline: deadline || null,
        reserveFromBudget: reserve && deadline !== '',
        note: null,
      })
      onDone()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вийшло зберегти.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <input
        value={person}
        onChange={(e) => setPerson(e.target.value)}
        placeholder={direction === 'IOwe' ? 'Кому винен' : 'Хто винен'}
        aria-label="Людина"
        className="w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2"
      />

      <div className="flex gap-2">
        <input
          inputMode="decimal"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          placeholder="Скільки"
          aria-label="Сума боргу"
          className="flex-1 min-w-0 rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2"
        />
        <select
          value={currency}
          onChange={(e) => setCurrency(e.target.value)}
          aria-label="Валюта боргу"
          className="rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-2 py-2"
        >
          {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>

      <label className="block text-xs text-neutral-400">
        Коли позичив
        <input
          type="date"
          value={date}
          onChange={(e) => setDate(e.target.value)}
          className="mt-1 w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2 text-base text-neutral-900 dark:text-neutral-100"
        />
      </label>

      <label className="block text-xs text-neutral-400">
        До якого числа (не обов'язково)
        <input
          type="date"
          value={deadline}
          onChange={(e) => setDeadline(e.target.value)}
          className="mt-1 w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2 text-base text-neutral-900 dark:text-neutral-100"
        />
      </label>

      {/* Only for money the user has to give back, and only with a date: there is nothing to
          divide by otherwise, and a switch that is on and does nothing is worse than none. */}
      {direction === 'IOwe' && deadline !== '' && (
        <label className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            checked={reserve}
            onChange={(e) => setReserve(e.target.checked)}
            className="mt-1"
          />
          <span>
            Відкладати щоперіоду
            <span className="block text-xs text-neutral-400">
              Сума ділиться на періоди до дедлайну і зникає з «Можна витратити сьогодні».
            </span>
          </span>
        </label>
      )}

      <FormError>{error}</FormError>
      <PrimaryButton onClick={save} disabled={busy}>Додати</PrimaryButton>
    </Card>
  )
}
