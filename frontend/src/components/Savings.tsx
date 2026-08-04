import { useEffect, useState } from 'react'
import type { BucketKind, EnvelopePeriod as EnvelopePeriodType, EnvelopeSummary, SaveEnvelope, SaveEnvelopeTarget, SaveTransfer, Savings as SavingsData, SaveSavingsEntry, SavingsEntry, SaveSavingsPlan } from '../types'
import { BASE_CURRENCY, CURRENCIES, todayIso } from '../types'
import { dayMonth, money } from '../format'
import { useEnvelopeHistory } from '../hooks'
import { WITHDRAWAL_ACTION, WITHDRAWAL_ICON, WITHDRAWAL_LABEL, envelopeIcon, envelopeWords } from '../envelopeWords'
import { Card, CardSkeleton, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

interface Props {
  data: SavingsData | null
  onSavePlan: (p: SaveSavingsPlan) => Promise<void>
  onAddEntry: (e: SaveSavingsEntry) => Promise<void>
  onUpdateEntry: (id: number, e: SaveSavingsEntry) => Promise<void>
  onDeleteEntry: (id: number) => Promise<void>
  onCreateEnvelope: (e: SaveEnvelope) => Promise<void>
  onUpdateEnvelope: (id: number, e: SaveEnvelope) => Promise<void>
  onArchiveEnvelope: (id: number) => Promise<void>
  onSetTarget: (id: number, t: SaveEnvelopeTarget) => Promise<void>
  onTransfer: (t: SaveTransfer) => Promise<void>
  /// Which jar is open — comes from the address, not from screen state.
  openId: number | null
  onOpen: (id: number | null) => void
  onBack: () => void
}

/// The kinds of jar in words. `Spending` is deliberately absent: money to spend is the
/// daily norm, not a jar.
const KINDS: { kind: BucketKind; label: string }[] = [
  { kind: 'Savings', label: 'Заощадження' },
  { kind: 'Investing', label: 'Інвестиції' },
  { kind: 'Debt', label: 'Борг' },
  { kind: 'Other', label: 'Інше' },
]

/// Two screens instead of one overloaded one. This used to hold, all at once, the jar chips,
/// the headline figure, a deposit/withdraw form, the plan form and the list of movements —
/// five blocks, with no way to tell which jar was on screen.
///
/// Now: a list of every jar → tap → one jar's screen with its history period by period. The
/// list answers "де скільки лежить"; the jar screen answers "за місяць скільки пішло і
/// скільки там тепер".
///
/// "Банка", not "конверт": the metaphor from monobank, which nobody has to be taught.
export function Savings({
  data, onSavePlan, onAddEntry, onUpdateEntry, onDeleteEntry,
  onCreateEnvelope, onUpdateEnvelope, onArchiveEnvelope, onSetTarget, onTransfer,
  openId, onOpen, onBack,
}: Props) {

  // The header stays while loading — the way back must not depend on the data arriving.
  if (!data) {
    return (
      <Screen title="Банки" onBack={onBack}>
        <CardSkeleton />
      </Screen>
    )
  }

  const open = data.envelopes.find((e) => e.id === openId)

  if (open) {
    return (
      <EnvelopeDetail
        envelope={open}
        data={data}
        onSavePlan={onSavePlan}
        onAddEntry={onAddEntry}
        onUpdateEntry={onUpdateEntry}
        onDeleteEntry={onDeleteEntry}
        onRename={onUpdateEnvelope}
        onSetTarget={onSetTarget}
        onTransfer={onTransfer}
        // An emptied jar goes away, and the screen goes with it: there is nothing left to look at.
        onArchive={async (id) => { await onArchiveEnvelope(id); onOpen(null) }}
        onBack={() => onOpen(null)}
      />
    )
  }

  return (
    <Screen
      title="Банки"
      onBack={onBack}
      footnote="Відкладене не входить у «Можна витратити сьогодні». Зняти можна будь-коли — це твої гроші, не податки."
    >
      <PausedNote pausedFrom={data.planPausedFrom} />
      <EnvelopeList envelopes={data.envelopes} currency={data.currency} onOpen={onOpen} />
      <NewEnvelope onCreate={onCreateEnvelope} />
    </Screen>
  )
}

/// The plan is standing down because the period began by counting the balance: that figure is
/// money to live on, and putting some of it aside again would halve the daily norm. Without
/// this line the screen shows a live plan next to a goal of 0 and looks broken.
function PausedNote({ pausedFrom }: { pausedFrom: string | null }) {
  if (!pausedFrom) return null

  return (
    <p className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 text-xs text-neutral-500">
      Цей період нічого не відкладаємо: {dayMonth(pausedFrom)} ти порахував залишок, і це
      гроші на життя до наступної зарплати. План знову вмикається з нею.
    </p>
  )
}

/// Everything at a glance: how much is put away in total, and where exactly it sits.
function EnvelopeList({ envelopes, currency, onOpen }: {
  envelopes: EnvelopeSummary[]
  currency: string
  onOpen: (id: number) => void
}) {
  const total = envelopes.reduce((s, e) => s + e.balance, 0)

  return (
    <>
      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center">
        <p className="text-sm uppercase tracking-wide text-neutral-400">Відкладено всього</p>
        <p className="mt-1 text-4xl font-bold tabular-nums">{money(total, currency)}</p>
      </div>

      <div className="space-y-2">
        {envelopes.map((e) => (
          <button
            key={e.id}
            onClick={() => onOpen(e.id)}
            className="w-full rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm text-left flex items-center justify-between gap-3"
          >
            <span className="text-xl shrink-0" aria-hidden>{envelopeIcon(e.kind)}</span>
            <span className="min-w-0 flex-1">
              <span className="block font-medium truncate">{e.name}</span>
              {/* One figure under the name — this period's movement. The goal and what is
                  still held back are gone: under a scheme they almost always match and add
                  nothing. */}
              <span className={`block text-xs tabular-nums ${e.depositedThisMonth < 0 ? 'text-red-600' : 'text-neutral-400'}`}>
                {e.depositedThisMonth === 0
                  ? 'цього періоду без змін'
                  : `${e.depositedThisMonth > 0 ? '+' : '−'}${money(Math.abs(e.depositedThisMonth), currency)} цього періоду`}
              </span>
            </span>
            <span className="tabular-nums font-semibold shrink-0">{money(e.balance, currency)}</span>
          </button>
        ))}
      </div>
    </>
  )
}

/// A jar for a goal of one's own. The word "банка" invites exactly this — and until now the
/// only way to get one was to open the allocation scheme and invent a percentage of income for
/// that holiday.
///
/// Collapsed to a single line until wanted: the jar list answers "де скільки лежить", and a
/// create form sitting above it would get in the way of reading the figures every day.
function NewEnvelope({ onCreate }: { onCreate: (e: SaveEnvelope) => Promise<void> }) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState('')
  const [kind, setKind] = useState<BucketKind>('Savings')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!open) {
    return (
      <button
        onClick={() => setOpen(true)}
        className="w-full rounded-2xl border border-dashed border-neutral-300 dark:border-neutral-700 px-4 py-3 text-sm text-neutral-500"
      >
        + Нова банка
      </button>
    )
  }

  async function create() {
    if (!name.trim() || busy) return
    setBusy(true)
    setError(null)
    try {
      await onCreate({ name: name.trim(), kind })
      setName('')
      setOpen(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося створити')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Нова банка</SectionTitle>
      <input
        type="text"
        autoFocus
        placeholder="Відпустка"
        value={name}
        onChange={(e) => setName(e.target.value)}
        className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 outline-none"
      />
      <KindPicker kind={kind} onPick={setKind} />
      <FormError>{error}</FormError>
      <div className="flex gap-2">
        <PrimaryButton onClick={create} disabled={!name.trim() || busy}>Створити</PrimaryButton>
        <button
          onClick={() => { setOpen(false); setError(null) }}
          className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2.5 text-sm"
        >
          Скасувати
        </button>
      </div>
      <p className="text-xs text-neutral-400">
        Банка починається з нуля й нічого не тримає з денної норми, поки ти сам туди не покладеш.
      </p>
    </Card>
  )
}

function KindPicker({ kind, onPick }: { kind: BucketKind; onPick: (k: BucketKind) => void }) {
  return (
    <div className="flex flex-wrap gap-2">
      {KINDS.map((k) => (
        <button
          key={k.kind}
          onClick={() => onPick(k.kind)}
          className={`rounded-xl px-3 py-2 text-sm ${
            kind === k.kind
              ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
              : 'bg-neutral-100 dark:bg-neutral-800'
          }`}
        >
          {k.label}
        </button>
      ))}
    </div>
  )
}

/// A target on a jar: "Відпустка 6 000 до червня" → "1 266,67 за період". Without it, a jar no
/// scheme feeds is a pointless piggy bank: money goes in and nothing says whether it is enough.
///
/// A target holds back **nothing** from the daily norm. Reserving automatically would compete
/// with the scheme for the same money and hold it twice — and, more importantly, the app would
/// be deciding for the user what their wish costs them today. What it gives is a pace to weigh
/// that up with.
function TargetCard({ envelope, currency, onSave }: {
  envelope: EnvelopeSummary
  currency: string
  onSave: (id: number, t: SaveEnvelopeTarget) => Promise<void>
}) {
  const target = envelope.target
  const [editing, setEditing] = useState(false)
  const [amount, setAmount] = useState(target ? String(target.amount) : '')
  const [date, setDate] = useState(target?.date ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const value = Number(amount.replace(',', '.'))

  async function run(payload: SaveEnvelopeTarget) {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await onSave(envelope.id, payload)
      setEditing(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setBusy(false)
    }
  }

  if (editing || !target) {
    if (!editing) {
      return (
        <button
          onClick={() => setEditing(true)}
          className="w-full rounded-2xl border border-dashed border-neutral-300 dark:border-neutral-700 px-4 py-3 text-sm text-neutral-500"
        >
          Поставити ціль
        </button>
      )
    }

    return (
      <Card>
        <SectionTitle>Ціль</SectionTitle>
        <div className="flex items-center gap-2">
          <input
            type="text"
            inputMode="decimal"
            autoFocus
            placeholder="6000"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="flex-1 text-2xl font-bold tabular-nums bg-transparent outline-none"
          />
          <span className="text-neutral-400 font-medium">{currency}</span>
        </div>
        {/* The date is optional: "зібрати 6 000" is a goal too, and demanding one would turn
            a wish into a plan with a deadline nobody set. */}
        <label className="flex items-center justify-between gap-3 text-sm text-neutral-500">
          До якої дати (необов'язково)
          <input
            type="date"
            value={date}
            min={todayIso()}
            onChange={(e) => setDate(e.target.value)}
            className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
          />
        </label>
        <FormError>{error}</FormError>
        <div className="flex gap-2">
          <PrimaryButton
            onClick={() => run({ amount: value, currency, date: date || null })}
            disabled={!(value > 0) || busy}
          >
            Зберегти
          </PrimaryButton>
          <button
            onClick={() => { setEditing(false); setError(null) }}
            className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2.5 text-sm"
          >
            Скасувати
          </button>
        </div>
      </Card>
    )
  }

  const share = target.amount > 0
    ? Math.min(100, Math.round(((target.amount - target.remaining) / target.amount) * 100))
    : 0

  return (
    <Card>
      <SectionTitle>Ціль</SectionTitle>
      <p className="text-sm">
        <span className="font-medium tabular-nums">{money(target.amount, currency)}</span>
        {target.date && <span className="text-neutral-500"> до {dayMonth(target.date)}</span>}
      </p>

      <div className="h-1.5 rounded-full bg-neutral-100 dark:bg-neutral-800 overflow-hidden">
        <div
          className={`h-full rounded-full ${target.reached ? 'bg-emerald-600' : 'bg-neutral-900 dark:bg-white'}`}
          style={{ width: `${share}%` }}
        />
      </div>

      <p className="text-xs text-neutral-500">
        {target.reached
          ? 'Ціль зібрана.'
          : target.overdue
            ? `Дата минула, а бракує ${money(target.remaining, currency)}.`
            : target.perPeriod > 0
              ? `Бракує ${money(target.remaining, currency)} — це ${money(target.perPeriod, currency)} за період, ${periodsWord(target.periodsLeft)}.`
              : `Бракує ${money(target.remaining, currency)}. Дати немає, тож і темпу немає — покладеш, коли буде.`}
      </p>
      <p className="text-xs text-neutral-400">
        Ціль нічого не тримає з «Можна витратити сьогодні» — вона лише показує темп.
      </p>

      <div className="flex gap-2">
        <button
          onClick={() => { setAmount(String(target.amount)); setDate(target.date ?? ''); setEditing(true) }}
          className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 text-sm"
        >
          Змінити ціль
        </button>
        <button
          onClick={() => run({ amount: null })}
          disabled={busy}
          className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 text-sm text-neutral-500 disabled:opacity-40"
        >
          Прибрати
        </button>
      </div>
    </Card>
  )
}

/// "3 періоди" / "4 періодів": a goal in the wrong case reads as machine output.
function periodsWord(count: number): string {
  const last = count % 10
  const tens = count % 100
  if (last === 1 && tens !== 11) return `${count} період`
  if (last >= 2 && last <= 4 && (tens < 12 || tens > 14)) return `${count} періоди`
  return `${count} періодів`
}

/// Renaming and putting away are for hand-made jars only. For the default jar and for a
/// scheme's jar alike, the NAME is how the app finds them: a rename would quietly hand the
/// balance to a jar nobody feeds, and putting one away would undo itself on the next screen
/// load.
function EnvelopeSettings({ envelope, currency, onRename, onArchive }: {
  envelope: EnvelopeSummary
  currency: string
  onRename: (id: number, e: SaveEnvelope) => Promise<void>
  onArchive: (id: number) => Promise<void>
}) {
  const [name, setName] = useState(envelope.name)
  const [kind, setKind] = useState<BucketKind>(envelope.kind)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => setSaved(false), [name, kind])

  if (envelope.isDefault || envelope.isFromScheme) {
    return (
      <p className="text-xs text-neutral-400 px-1">
        {envelope.isDefault
          ? 'Це банка за замовчуванням: гроші, для яких не вибрали банку, йдуть сюди. Назву застосунок шукає сам, тому вона незмінна.'
          : 'Назву й ціль цієї банки задає схема розподілу — перейменуй кошик у схемі, і банка перейменується разом із ним.'}
      </p>
    )
  }

  const changed = name.trim() !== envelope.name || kind !== envelope.kind
  const empty = envelope.balance === 0

  async function run(fn: () => Promise<void>) {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await fn()
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Назва й вид</SectionTitle>
      <input
        type="text"
        value={name}
        onChange={(e) => setName(e.target.value)}
        className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 outline-none"
      />
      <KindPicker kind={kind} onPick={setKind} />
      <FormError>{error}</FormError>
      <PrimaryButton
        onClick={() => run(() => onRename(envelope.id, { name: name.trim(), kind }))}
        disabled={!name.trim() || !changed || busy}
        saved={saved}
      >
        Зберегти назву
      </PrimaryButton>

      {/* Empty ones only: a jar that vanished with money inside would take it out of
          "Відкладено всього" — the one figure this app asks to be trusted. */}
      {empty ? (
        <button
          onClick={() => run(() => onArchive(envelope.id))}
          disabled={busy}
          className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 text-sm text-red-600 disabled:opacity-40"
        >
          Прибрати банку
        </button>
      ) : (
        <p className="text-xs text-neutral-400">
          Щоб прибрати банку, спорожни її: у ній ще {money(envelope.balance, currency)}. Рухи
          нікуди не зникнуть — прибрана банка просто йде зі списку, і повертається, якщо
          створити її з тією ж назвою.
        </p>
      )}
    </Card>
  )
}

/// One jar: its balance, its history period by period, and everything that can be done to it.
function EnvelopeDetail({ envelope, data, onSavePlan, onAddEntry, onUpdateEntry, onDeleteEntry, onRename, onSetTarget, onTransfer, onArchive, onBack }: {
  envelope: EnvelopeSummary
  data: SavingsData
  onSavePlan: (p: SaveSavingsPlan) => Promise<void>
  onAddEntry: (e: SaveSavingsEntry) => Promise<void>
  onUpdateEntry: (id: number, e: SaveSavingsEntry) => Promise<void>
  onDeleteEntry: (id: number) => Promise<void>
  onRename: (id: number, e: SaveEnvelope) => Promise<void>
  onSetTarget: (id: number, t: SaveEnvelopeTarget) => Promise<void>
  onTransfer: (t: SaveTransfer) => Promise<void>
  onArchive: (id: number) => Promise<void>
  onBack: () => void
}) {
  // Which movement is open for editing — at most one, so the list stays readable.
  const [editing, setEditing] = useState<SavingsEntry | null>(null)
  const history = useEnvelopeHistory(envelope.id)

  return (
    <Screen title={envelope.name} onBack={onBack}>
      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm text-center">
        <p className="text-sm uppercase tracking-wide text-neutral-400">Зараз у банці</p>
        <p className="mt-1 text-4xl font-bold tabular-nums">{money(envelope.balance, data.currency)}</p>
        {envelope.monthGoal > 0 && (
          <p className="mt-2 text-xs text-neutral-400">
            За планом сюди йде {money(envelope.monthGoal, data.currency)} кожного періоду — додаток
            відкладає це сам.
          </p>
        )}
      </div>

      <TargetCard envelope={envelope} currency={data.currency} onSave={onSetTarget} />

      <PeriodHistory periods={history.data ?? []} currency={data.currency} />

      <MoveMoney
        currency={data.currency}
        balance={envelope.balance}
        envelopeId={envelope.id}
        kind={envelope.kind}
        onAdd={onAddEntry}
      />

      <MoveToAnotherJar
        from={envelope}
        jars={data.envelopes}
        currency={data.currency}
        onTransfer={onTransfer}
      />

      {/* Default jar only: for the rest the scheme dictates the goal, and a plan form would
          promise a change that does nothing. */}
      {envelope.isDefault && <PlanForm data={data} onSave={onSavePlan} />}

      <EnvelopeSettings
        envelope={envelope}
        currency={data.currency}
        onRename={onRename}
        onArchive={onArchive}
      />

      <History
        data={data}
        envelopeId={envelope.id}
        kind={envelope.kind}
        editing={editing}
        onEdit={setEditing}
        onSave={async (id, e) => { await onUpdateEntry(id, e); setEditing(null) }}
        onDelete={onDeleteEntry}
      />
    </Screen>
  )
}

/// The reason this screen was rebuilt: per period, what moved and what the balance became.
function PeriodHistory({ periods, currency }: { periods: EnvelopePeriodType[]; currency: string }) {
  if (periods.length === 0) return null

  return (
    <Card>
      <SectionTitle>По періодах</SectionTitle>
      <dl className="text-sm">
        {periods.map((p) => (
          <div key={p.start} className="flex items-baseline justify-between gap-3 py-1.5">
            <dt className="text-neutral-500 shrink-0">{dayMonth(p.start)} – {dayMonth(p.end)}</dt>
            <dd className="flex items-baseline gap-3 tabular-nums">
              <span className={p.moved > 0 ? 'text-emerald-600' : p.moved < 0 ? 'text-red-600' : 'text-neutral-400'}>
                {p.moved === 0 ? '—' : `${p.moved > 0 ? '+' : '−'}${money(Math.abs(p.moved), currency)}`}
              </span>
              <span className="font-medium min-w-[5.5rem] text-right">{money(p.balanceAfter, currency)}</span>
            </dd>
          </div>
        ))}
      </dl>
    </Card>
  )
}

/// Move money to another jar. By hand this was two movements — withdraw here, deposit there —
/// and in between the money existed nowhere; forget the second one and it stayed that way. Now
/// it is one act, and it is undone as one.
function MoveToAnotherJar({ from, jars, currency, onTransfer }: {
  from: EnvelopeSummary
  jars: EnvelopeSummary[]
  currency: string
  onTransfer: (t: SaveTransfer) => Promise<void>
}) {
  const others = jars.filter((e) => e.id !== from.id)
  const [open, setOpen] = useState(false)
  const [toId, setToId] = useState<number | null>(others[0]?.id ?? null)
  const [amount, setAmount] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Neither an empty jar nor the only jar can transfer anywhere — an action that would walk
  // straight into a refusal is not offered.
  if (others.length === 0 || from.balance <= 0) return null

  const value = Number(amount.replace(',', '.'))
  const valid = value > 0 && value <= from.balance && toId !== null

  if (!open) {
    return (
      <button
        onClick={() => setOpen(true)}
        className="w-full rounded-2xl border border-dashed border-neutral-300 dark:border-neutral-700 px-4 py-3 text-sm text-neutral-500"
      >
        Перекинути в іншу банку
      </button>
    )
  }

  async function move() {
    if (!valid || busy || toId === null) return
    setBusy(true)
    setError(null)
    try {
      await onTransfer({ fromEnvelopeId: from.id, toEnvelopeId: toId, amount: value, currency })
      setAmount('')
      setOpen(false)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося перекинути')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Перекинути в іншу банку</SectionTitle>
      <div className="flex items-center gap-2">
        <input
          type="text"
          inputMode="decimal"
          autoFocus
          placeholder="0"
          aria-label="Скільки перекинути"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          className="flex-1 text-2xl font-bold tabular-nums bg-transparent outline-none"
        />
        <span className="text-neutral-400 font-medium">{currency}</span>
      </div>
      <label className="flex items-center justify-between gap-3 text-sm text-neutral-500">
        Куди
        <select
          value={toId ?? ''}
          onChange={(e) => setToId(Number(e.target.value))}
          className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        >
          {others.map((e) => (
            <option key={e.id} value={e.id}>{e.name}</option>
          ))}
        </select>
      </label>
      <FormError>{error}</FormError>
      <div className="flex gap-2">
        <PrimaryButton onClick={move} disabled={!valid || busy}>Перекинути</PrimaryButton>
        <button
          onClick={() => { setOpen(false); setError(null) }}
          className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2.5 text-sm"
        >
          Скасувати
        </button>
      </div>
      <p className="text-xs text-neutral-400">
        Максимум: {money(from.balance, currency)}. «Відкладено всього» від цього не зміниться —
        гроші просто перекладаються з однієї банки в іншу.
      </p>
    </Card>
  )
}

/// A movement made by hand. The scheme puts money aside on its own, so a deposit from here is
/// "понад план" — and it really does take its own amount off "можна витратити сьогодні".
function MoveMoney({ currency, balance, envelopeId, kind, onAdd }: {
  currency: string
  balance: number
  envelopeId: number | undefined
  kind: BucketKind
  onAdd: (e: SaveSavingsEntry) => Promise<void>
}) {
  const words = envelopeWords(kind)
  const [amount, setAmount] = useState('')
  const [entryCurrency, setEntryCurrency] = useState<string>(BASE_CURRENCY)
  const [note, setNote] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const value = Number(amount.replace(',', '.'))
  const valid = value > 0
  const isBase = entryCurrency === BASE_CURRENCY

  async function move(kind: 'Deposit' | 'Withdrawal') {
    if (!valid || busy) return
    setBusy(true)
    setError(null)
    try {
      await onAdd({ kind, amount: value, currency: entryCurrency, note: note.trim() || null, envelopeId })
      setAmount('')
      setNote('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Змінити вручну</SectionTitle>

      <div className="flex gap-2">
        <input
          type="text"
          inputMode="decimal"
          placeholder="0"
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          className="flex-1 text-3xl font-bold tabular-nums bg-transparent outline-none w-full"
        />
        <select
          value={entryCurrency}
          onChange={(e) => setEntryCurrency(e.target.value)}
          className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 font-medium"
        >
          {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>
      <input
        type="text"
        placeholder="Нотатка (необов'язково)"
        value={note}
        onChange={(e) => setNote(e.target.value)}
        className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
      />

      <FormError>{error}</FormError>

      <div className="flex gap-2">
        <button
          disabled={!valid || busy}
          onClick={() => move('Deposit')}
          className="flex-1 rounded-xl bg-emerald-600 text-white px-3 py-2.5 font-medium disabled:opacity-40"
        >
          + {words.depositAction}
        </button>
        <button
          // In another currency the limit is only known after conversion, so the check is
          // the server's; blocking here on a base-currency balance would be a guess.
          disabled={!valid || busy || (isBase && value > balance)}
          onClick={() => move('Withdrawal')}
          className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 font-medium disabled:opacity-40"
        >
          − {WITHDRAWAL_ACTION}
        </button>
      </div>
      <p className="text-xs text-neutral-400">
        Максимум до зняття: {money(balance, currency)}.
        {!isBase && ` Сума в ${entryCurrency} перерахується за курсом на сьогодні.`}
      </p>
    </Card>
  )
}

function PlanForm({ data, onSave }: { data: SavingsData; onSave: (p: SaveSavingsPlan) => Promise<void> }) {
  const [mode, setMode] = useState<'Fixed' | 'Percent'>(data.mode)
  const [value, setValue] = useState(data.value > 0 ? String(data.value) : '')
  const [active, setActive] = useState(data.active)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)

  // Any edit invalidates the "Збережено ✓" the button is still showing.
  useEffect(() => setSaved(false), [mode, value, active])

  const num = Number(value.replace(',', '.'))
  const valid = num >= 0 && (mode !== 'Percent' || num <= 100)

  async function save() {
    if (!valid || busy) return
    setBusy(true)
    try {
      await onSave({ mode, value: num, active })
      setSaved(true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Скільки у заощадження щомісяця</SectionTitle>

      {data.goalFromScheme && (
        <p className="rounded-xl bg-amber-50 dark:bg-amber-950 text-amber-700 dark:text-amber-300 px-3 py-2 text-xs">
          Ціль зараз задає схема «{data.goalFromScheme}» — {money(data.monthGoal, data.currency)} на місяць.
          Те, що вписано нижче, не діє, поки в схемі є кошик заощаджень.
        </p>
      )}

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

      <PrimaryButton onClick={save} disabled={!valid || busy} saved={saved}>
        Зберегти
      </PrimaryButton>
    </Card>
  )
}

function History({ data, envelopeId, kind, editing, onEdit, onSave, onDelete }: {
  data: SavingsData
  envelopeId: number | undefined
  kind: BucketKind
  editing: SavingsEntry | null
  onEdit: (e: SavingsEntry | null) => void
  onSave: (id: number, e: SaveSavingsEntry) => Promise<void>
  onDelete: (id: number) => Promise<void>
}) {
  // This jar's movements only: a combined list would not add up to the balance above it.
  const rows = data.recent.filter((e) => e.envelopeId === envelopeId)
  if (rows.length === 0) return null

  return (
    <div>
      <h2 className="text-sm font-medium text-neutral-400 mb-2 px-1">Рухи</h2>
      <ul className="space-y-2">
        {rows.map((e) => (
          <li key={e.id} className="rounded-xl bg-white dark:bg-neutral-900 px-4 py-3 shadow-sm">
            {editing?.id === e.id ? (
              <EditEntry entry={e} onSave={(payload) => onSave(e.id, payload)} onCancel={() => onEdit(null)} />
            ) : (
              <div className="flex items-center gap-3">
                <span className="text-xl" aria-hidden>
                  {e.kind === 'Deposit' ? envelopeIcon(kind) : WITHDRAWAL_ICON}
                </span>
                {/* A deposit the scheme made is neither editable nor deletable: the next
                    screen load would bring it back, and the action would look like it undid
                    itself. For a different amount, change the scheme or the plan. */}
                <button
                  onClick={() => onEdit(e)}
                  disabled={e.isAuto || e.isTransfer}
                  className="flex-1 min-w-0 text-left disabled:cursor-default"
                >
                  <p className="font-medium truncate">{label(e, kind)}</p>
                  <p className="text-xs text-neutral-400 truncate">
                    {e.isAuto
                      ? 'за схемою'
                      : e.note || (e.date === todayIso() ? 'сьогодні' : e.date)}
                    {/* Only worth showing when it differs from the balance's currency. */}
                    {e.currencyOriginal !== data.currency && ` · ${money(e.amountOriginal, e.currencyOriginal)}`}
                  </p>
                </button>
                <p className={`font-semibold tabular-nums ${e.kind === 'Deposit' ? 'text-emerald-600' : ''}`}>
                  {e.kind === 'Deposit' ? '+' : '−'}{money(e.amount, data.currency)}
                </p>
                {e.isAuto
                  ? <span className="px-1 text-neutral-300 dark:text-neutral-700" aria-hidden>🔒</span>
                  : (
                    <button onClick={() => onDelete(e.id)} className="text-neutral-300 hover:text-red-500 px-1" aria-label="Видалити">
                      ✕
                    </button>
                  )}
              </div>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}

/// A movement's label follows the jar's kind: "Внесок" into savings, "Погашення" into debt,
/// "Інвестовано" into investments. Every deposit into every jar used to read "У заощадження",
/// which in a debt jar looked like a bug.
function label(entry: SavingsEntry, kind: BucketKind): string {
  // A transfer is called a move, not a deposit: otherwise the same shuffle would read as two
  // unrelated events on two screens — a deposit here and a withdrawal there.
  if (entry.isTransfer) return entry.kind === 'Deposit' ? 'Перекинуто сюди' : 'Перекинуто звідси'
  return entry.kind === 'Deposit' ? envelopeWords(kind).deposit : WITHDRAWAL_LABEL
}

/// Editing keeps the original currency: the entry is a record of what was moved, and
/// re-converting it at today's rate would quietly rewrite history.
function EditEntry({ entry, onSave, onCancel }: {
  entry: SavingsEntry
  onSave: (e: SaveSavingsEntry) => Promise<void>
  onCancel: () => void
}) {
  const [amount, setAmount] = useState(String(entry.amountOriginal))
  const [note, setNote] = useState(entry.note ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const value = Number(amount.replace(',', '.'))

  async function save() {
    if (!(value > 0) || busy) return
    setBusy(true)
    setError(null)
    try {
      await onSave({
        kind: entry.kind,
        amount: value,
        currency: entry.currencyOriginal,
        date: entry.date,
        note: note.trim() || null,
      })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
      setBusy(false)
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <input
          type="text"
          inputMode="decimal"
          autoFocus
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          className="flex-1 text-2xl font-bold tabular-nums bg-transparent outline-none"
        />
        <span className="text-neutral-400 font-medium">{entry.currencyOriginal}</span>
      </div>
      <input
        type="text"
        placeholder="Нотатка"
        value={note}
        onChange={(e) => setNote(e.target.value)}
        className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
      />
      {error && <p className="text-sm text-red-600">{error}</p>}
      <div className="flex gap-2">
        <button
          disabled={!(value > 0) || busy}
          onClick={save}
          className="flex-1 rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-3 py-2 text-sm font-medium disabled:opacity-40"
        >
          Зберегти
        </button>
        <button onClick={onCancel} className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2 text-sm">
          Скасувати
        </button>
      </div>
    </div>
  )
}
