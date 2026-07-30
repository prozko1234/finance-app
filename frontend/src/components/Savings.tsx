import { useEffect, useState } from 'react'
import type { BucketKind, EnvelopePeriod as EnvelopePeriodType, EnvelopeSummary, SaveEnvelope, SaveEnvelopeTarget, Savings as SavingsData, SaveSavingsEntry, SavingsEntry, SaveSavingsPlan } from '../types'
import { BASE_CURRENCY, CURRENCIES, todayIso } from '../types'
import { dayMonth, money } from '../format'
import { useEnvelopeHistory } from '../hooks'
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
  onBack: () => void
}

/// Види банки словами. `Spending` тут немає свідомо: гроші на витрати — це денна норма.
const KINDS: { kind: BucketKind; label: string }[] = [
  { kind: 'Savings', label: 'Заощадження' },
  { kind: 'Investing', label: 'Інвестиції' },
  { kind: 'Debt', label: 'Борг' },
  { kind: 'Other', label: 'Інше' },
]

/// Два екрани замість одного перевантаженого. Раніше тут одночасно жили чипси вибору,
/// велика цифра, форма «покласти/зняти», форма плану і список рухів — п'ять блоків, і
/// незрозуміло, яка банка зараз на екрані.
///
/// Тепер: список усіх банок → тап → екран однієї банки з історією по періодах. Списком
/// відповідаємо на «де скільки лежить», екраном банки — на «за місяць скільки пішло і
/// скільки там тепер».
///
/// «Банка», не «конверт»: метафора з монобанку, яку не треба вчити.
export function Savings({
  data, onSavePlan, onAddEntry, onUpdateEntry, onDeleteEntry,
  onCreateEnvelope, onUpdateEnvelope, onArchiveEnvelope, onSetTarget, onBack,
}: Props) {
  const [openId, setOpenId] = useState<number | null>(null)

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
        // Порожня банка зникає — і екран разом із нею, бо дивитись уже нема на що.
        onArchive={async (id) => { await onArchiveEnvelope(id); setOpenId(null) }}
        onBack={() => setOpenId(null)}
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
      <EnvelopeList envelopes={data.envelopes} currency={data.currency} onOpen={setOpenId} />
      <NewEnvelope onCreate={onCreateEnvelope} />
    </Screen>
  )
}

/// План стоїть, бо період почався з підрахунку залишку: та сума — це гроші на життя, і
/// відкласти з них ще раз означало б порізати денну норму навпіл. Без цього рядка екран
/// показує живий план поруч із ціллю 0 і виглядає зламаним.
function PausedNote({ pausedFrom }: { pausedFrom: string | null }) {
  if (!pausedFrom) return null

  return (
    <p className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 text-xs text-neutral-500">
      Цей період нічого не відкладаємо: {dayMonth(pausedFrom)} ти порахував залишок, і це
      гроші на життя до наступної зарплати. План знову вмикається з нею.
    </p>
  )
}

/// Усе видно одразу: скільки відкладено разом і де саме воно лежить.
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
            className="w-full rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm text-left flex items-baseline justify-between gap-3"
          >
            <span className="min-w-0">
              <span className="block font-medium truncate">{e.name}</span>
              {/* Одне число під назвою — рух саме цього періоду. Ціль і «ще тримається»
                  прибрані: під схемою вони майже завжди збігаються й нічого не додають. */}
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

/// Банка під власну ціль. Слово «банка» саме запрошує зробити банку на відпустку — а до цього
/// єдиним способом було зайти в схему розподілу й вигадати тій відпустці відсоток від доходу.
///
/// Згорнута в один рядок, поки не потрібна: список банок — це відповідь на «де скільки лежить»,
/// і форма створення над ним щодня заважала б читати цифри.
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

/// Ціль на банку: «Відпустка 6 000 до червня» → «1 266,67 за період». Без цього банка, яку не
/// годує схема, — скарбничка без сенсу: гроші заходять, і ніщо не каже, чи цього досить.
///
/// Ціль **нічого не тримає** з денної норми. Автоматичне резервування змагалося б зі схемою за
/// ті самі гроші й тримало б їх двічі — а головне, застосунок вирішував би за людину, скільки
/// її бажання коштує їй сьогодні. Тут лише темп, з яким вона зважує сама.
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
        {/* Дата необовʼязкова: «зібрати 6 000» — теж ціль, а вимагати дату означало б
            перетворити бажання на план із дедлайном, якого людина не ставила. */}
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

/// «3 періоди» / «4 періодів»: ціль без правильного відмінка читається як машинний вивід.
function periodsWord(count: number): string {
  const last = count % 10
  const tens = count % 100
  if (last === 1 && tens !== 11) return `${count} період`
  if (last >= 2 && last <= 4 && (tens < 12 || tens > 14)) return `${count} періоди`
  return `${count} періодів`
}

/// Перейменувати й прибрати — тільки для банки, зробленої руками. І в банки за замовчуванням,
/// і в банки зі схеми назва — це те, за чим застосунок їх знаходить: перейменування тихо
/// віддало б баланс банці, яку ніхто не наповнює, а прибирання скасувалось би саме собою при
/// наступному завантаженні екрана.
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

      {/* Тільки порожню: банка, що зникла з грошима всередині, забрала б їх із «Відкладено
          всього» — тобто з тієї єдиної цифри, якій застосунок просить вірити. */}
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

/// Одна банка: баланс, історія по періодах і все, що з нею можна зробити.
function EnvelopeDetail({ envelope, data, onSavePlan, onAddEntry, onUpdateEntry, onDeleteEntry, onRename, onSetTarget, onArchive, onBack }: {
  envelope: EnvelopeSummary
  data: SavingsData
  onSavePlan: (p: SaveSavingsPlan) => Promise<void>
  onAddEntry: (e: SaveSavingsEntry) => Promise<void>
  onUpdateEntry: (id: number, e: SaveSavingsEntry) => Promise<void>
  onDeleteEntry: (id: number) => Promise<void>
  onRename: (id: number, e: SaveEnvelope) => Promise<void>
  onSetTarget: (id: number, t: SaveEnvelopeTarget) => Promise<void>
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
        onAdd={onAddEntry}
      />

      {/* Тільки для банки за замовчуванням: решті ціль диктує схема, і форма плану
          обіцяла б зміну, яка нічого не робить. */}
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
        editing={editing}
        onEdit={setEditing}
        onSave={async (id, e) => { await onUpdateEntry(id, e); setEditing(null) }}
        onDelete={onDeleteEntry}
      />
    </Screen>
  )
}

/// Те, заради чого екран переробляли: за кожен період — скільки пішло і скільки стало.
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

/// Рух руками. Схема відкладає сама, тож внесок звідси — це «понад план», і він справді
/// зменшує «можна витратити сьогодні» на свою суму.
function MoveMoney({ currency, balance, envelopeId, onAdd }: {
  currency: string
  balance: number
  envelopeId: number | undefined
  onAdd: (e: SaveSavingsEntry) => Promise<void>
}) {
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
          + Відкласти
        </button>
        <button
          // In another currency the limit is only known after conversion, so the check is
          // the server's; blocking here on a base-currency balance would be a guess.
          disabled={!valid || busy || (isBase && value > balance)}
          onClick={() => move('Withdrawal')}
          className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 font-medium disabled:opacity-40"
        >
          − Зняти
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

function History({ data, envelopeId, editing, onEdit, onSave, onDelete }: {
  data: SavingsData
  envelopeId: number | undefined
  editing: SavingsEntry | null
  onEdit: (e: SavingsEntry | null) => void
  onSave: (id: number, e: SaveSavingsEntry) => Promise<void>
  onDelete: (id: number) => Promise<void>
}) {
  // Рухи саме цієї банки: список усіх разом не сходився б із балансом над ним.
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
                <span className="text-xl">{e.kind === 'Deposit' ? '🐖' : '↩️'}</span>
                {/* Внесок за схемою не редагується і не видаляється: наступне завантаження
                    екрана привело б його назад, і дія виглядала б так, ніби скасувалась
                    сама. Хочеш іншу суму — міняй схему або план. */}
                <button
                  onClick={() => onEdit(e)}
                  disabled={e.isAuto}
                  className="flex-1 min-w-0 text-left disabled:cursor-default"
                >
                  <p className="font-medium truncate">{label(e)}</p>
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

/// Підпис руху. Раніше кожен внесок у будь-яку банку звався «У заощадження» — у банці
/// «Борг» це читалось як помилка додатка.
function label(entry: SavingsEntry): string {
  return entry.kind === 'Deposit' ? 'Внесок' : 'Знято'
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
