import { useState } from 'react'
import type { Category, Recurring as RecurringType, SaveRecurring } from '../types'
import { CURRENCIES, todayIso } from '../types'
import { CADENCES, DEFAULT_CADENCE, perMonth, sameCadence, scheduleSummary, type Cadence } from '../cadence'
import { daysUntil, dayMonth, money, signedMoney, signedMoneyClass } from '../format'
import { Screen } from './Screen'

interface Props {
  categories: Category[]
  items: RecurringType[]
  onCreate: (r: SaveRecurring) => Promise<void>
  /// Виправлення підписки замість «видалити й ввести заново». `PUT` був давно, не було
  /// лише способу його покликати.
  onUpdate: (id: number, r: SaveRecurring) => Promise<void>
  onToggle: (r: RecurringType) => void
  onDelete: (id: number) => Promise<void>
  onBack: () => void
}

/// A row waiting to be saved. `key` is local-only: drafts have no server id yet,
/// and using the array index would make removals re-render the wrong row.
interface Draft extends SaveRecurring {
  key: number
  categoryName: string
}

let nextDraftKey = 1

export function Recurring({ categories, items, onCreate, onUpdate, onToggle, onDelete, onBack }: Props) {
  const [amount, setAmount] = useState('')
  const [currency, setCurrency] = useState('PLN')
  const [categoryId, setCategoryId] = useState<number | null>(categories[0]?.id ?? null)
  const [cadence, setCadence] = useState<Cadence>(DEFAULT_CADENCE)
  // Дата першого списання, а не «число місяця»: для щотижневої підписки числа не існує,
  // а день тижня береться саме звідси.
  const [startsOn, setStartsOn] = useState(todayIso)
  const [note, setNote] = useState('')
  const [drafts, setDrafts] = useState<Draft[]>([])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Яку підписку зараз правимо. Форма та сама, що й для створення: інша форма для тих самих
  // полів — це друге місце, де їх доведеться міняти.
  const [editing, setEditing] = useState<RecurringType | null>(null)
  // An empty screen has nothing to look at, so the form is already open there. With rows on
  // it, the screen's job is answering «що в мене списується і скільки це коштує» — the form
  // is a thing you go to a few times a year, and it was covering the answer.
  const [adding, setAdding] = useState(items.length === 0)

  function edit(r: RecurringType) {
    setEditing(r)
    setAdding(true)
    setAmount(String(r.amountOriginal))
    setCurrency(r.currencyOriginal)
    setCategoryId(r.categoryId)
    setCadence(CADENCES.find((c) => sameCadence(r, c)) ?? { unit: r.unit, interval: r.interval, label: '' })
    setStartsOn(r.startsOn)
    setNote(r.note ?? '')
    setDrafts([])
    setError(null)
    // Форма живе над списком: без цього тап по рядку виглядав би так, ніби нічого не сталось.
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  function stopEditing() {
    setEditing(null)
    setAdding(items.length === 0)
    setAmount('')
    setNote('')
    setError(null)
  }

  async function saveEdit() {
    if (!valid || categoryId === null || !editing) return
    setSaving(true)
    setError(null)
    try {
      await onUpdate(editing.id, {
        amount: amountNum,
        currency,
        categoryId,
        startsOn,
        unit: cadence.unit,
        interval: cadence.interval,
        note: note.trim() || null,
        // Пауза й вид (дохід чи витрата) не в цій формі — вони лишаються, якими були.
        active: editing.active,
        kind: editing.kind,
        amountIncludesVat: editing.amountIncludesVat,
      })
      stopEditing()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setSaving(false)
    }
  }

  const amountNum = Number(amount.replace(',', '.'))
  const valid = amountNum > 0 && categoryId !== null && startsOn !== ''
  const pending = drafts.length + (valid ? 1 : 0)

  /// Currency, category and day usually repeat across a batch; the amount and the
  /// name never do, so only those are cleared between rows.
  function stage(): Draft[] | null {
    if (!valid || categoryId === null) return null
    const row: Draft = {
      key: nextDraftKey++,
      amount: amountNum,
      currency,
      categoryId,
      categoryName: categories.find((c) => c.id === categoryId)?.name ?? '',
      startsOn,
      unit: cadence.unit,
      interval: cadence.interval,
      note: note.trim() || null,
      active: true,
    }
    setAmount('')
    setNote('')
    return [...drafts, row]
  }

  function addAnother() {
    const staged = stage()
    if (staged) setDrafts(staged)
  }

  /// One row at a time, keeping only what failed: retrying after a partial failure
  /// must not create a second copy of the rows that already went through.
  async function saveAll() {
    const rows = stage() ?? drafts
    if (rows.length === 0) return
    setSaving(true)
    setError(null)
    let left = rows
    try {
      for (const row of rows) {
        const { key: _key, categoryName: _name, ...payload } = row
        await onCreate(payload)
        left = left.filter((r) => r.key !== row.key)
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setDrafts(left)
      setSaving(false)
      if (left.length === 0) setAdding(false)
    }
  }

  return (
    <Screen
      title="Регулярні: підписки й дохід"
      onBack={onBack}
      footnote="Те, що списується регулярно, застосунок додає сам — і тримає з бюджету наперед, щоб денна норма не обіцяла грошей, які вже обіцяні."
    >

      <MonthlyCost items={items} />

      {!adding ? (
        <button
          onClick={() => setAdding(true)}
          className="w-full rounded-2xl border border-dashed border-neutral-300 dark:border-neutral-700 px-4 py-3 text-sm text-neutral-500"
        >
          + Додати підписку чи регулярний дохід
        </button>
      ) : (
      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-3">
        {editing && (
          <p className="text-sm font-medium text-neutral-400">
            Редагуємо «{editing.note || editing.categoryName}»
          </p>
        )}
        <div className="flex gap-2">
          <input
            inputMode="decimal" placeholder="0" value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="flex-1 text-2xl font-bold tabular-nums bg-transparent outline-none"
          />
          <select
            value={currency} onChange={(e) => setCurrency(e.target.value)}
            className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 font-medium"
          >
            {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        {/* Two dropdowns instead of two walls of chips. A category list grown from a year of
            statements is thirty-odd buttons, and every cadence was on screen at once as well —
            forty choices to add one subscription, when the answer is almost always the same
            two. A dropdown shows the current answer and hides the rest until asked. */}
        <div className="flex gap-2">
          <select
            value={categoryId ?? ''} onChange={(e) => setCategoryId(Number(e.target.value))}
            aria-label="Категорія"
            className="flex-1 min-w-0 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm"
          >
            {categories.map((c) => (
              <option key={c.id} value={c.id}>{c.icon ? `${c.icon} ` : ''}{c.name}</option>
            ))}
          </select>
          <select
            value={`${cadence.unit}-${cadence.interval}`}
            onChange={(e) => setCadence(CADENCES.find((c) => `${c.unit}-${c.interval}` === e.target.value)!)}
            aria-label="Як часто"
            className="flex-1 min-w-0 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm"
          >
            {CADENCES.map((c) => (
              <option key={`${c.unit}-${c.interval}`} value={`${c.unit}-${c.interval}`}>{c.label}</option>
            ))}
          </select>
        </div>

        <div className="flex items-center gap-2 text-sm">
          <span className="text-neutral-500 shrink-0">Перше списання</span>
          <input
            type="date" value={startsOn} onChange={(e) => setStartsOn(e.target.value)}
            className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5"
          />
        </div>
        {/* The schedule in words: "13 серпня" on its own does not say whether that is a
            Tuesday, nor whether it comes back every week. */}
        <p className="text-xs text-neutral-400">
          Списуватиметься {scheduleSummary(cadence.unit, cadence.interval, startsOn)}.
        </p>

        <input
          placeholder="Назва (Netflix, оренда…)" value={note}
          onChange={(e) => setNote(e.target.value)}
          className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />

        {!editing && drafts.length > 0 && (
          <ul className="space-y-1">
            {drafts.map((d) => (
              <li key={d.key} className="flex items-center gap-2 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm">
                <span className="flex-1 min-w-0 truncate">{d.note || d.categoryName}</span>
                <span className="text-xs text-neutral-400">
                  {scheduleSummary(d.unit ?? 'Month', d.interval ?? 1, d.startsOn)}
                </span>
                <span className="font-medium tabular-nums">{money(d.amount, d.currency)}</span>
                <button
                  onClick={() => setDrafts(drafts.filter((x) => x.key !== d.key))}
                  className="text-neutral-400 hover:text-red-500 px-1" aria-label="Прибрати з черги"
                >
                  ✕
                </button>
              </li>
            ))}
          </ul>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}

        {editing ? (
          <div className="flex gap-2">
            <button
              onClick={saveEdit} disabled={!valid || saving}
              className="flex-1 rounded-xl bg-emerald-600 text-white py-2.5 font-medium disabled:opacity-40"
            >
              {saving ? 'Зберігаю…' : 'Зберегти зміни'}
            </button>
            <button
              onClick={stopEditing}
              className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2.5"
            >
              Скасувати
            </button>
          </div>
        ) : (
          <div className="flex gap-2">
            {items.length > 0 && (
              <button
                onClick={() => { setDrafts([]); setAmount(''); setNote(''); setAdding(false) }}
                className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2.5 text-neutral-500"
              >
                Закрити
              </button>
            )}
            <button
              onClick={addAnother} disabled={!valid || saving}
              className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2.5 font-medium disabled:opacity-40"
            >
              + Ще одна
            </button>
            <button
              onClick={saveAll} disabled={pending === 0 || saving}
              className="flex-1 rounded-xl bg-emerald-600 text-white py-2.5 font-medium disabled:opacity-40"
            >
              {saving ? 'Зберігаю…' : pending > 1 ? `Зберегти (${pending})` : 'Зберегти'}
            </button>
          </div>
        )}
      </div>
      )}

      {items.length === 0 ? (
        <p className="text-center text-neutral-400 text-sm">Ще немає нічого регулярного.</p>
      ) : (
        <ul className="space-y-2">
          {items.map((r) => (
            <li
              key={r.id}
              className={`flex items-center gap-3 rounded-xl px-4 py-3 shadow-sm bg-white dark:bg-neutral-900 ${
                r.active ? '' : 'opacity-50'
              }`}
            >
              <button onClick={() => edit(r)} className="flex-1 min-w-0 text-left">
                <p className="font-medium truncate">{r.note || r.categoryName}</p>
                <p className="text-xs text-neutral-400">
                  {whenNext(r)} · {r.kind === 'Income' ? 'дохід' : r.categoryName}
                </p>
              </button>
              <p className={`font-semibold tabular-nums ${signedMoneyClass(r.kind)}`}>
                {signedMoney(r.amountOriginal, r.currencyOriginal, r.kind)}
              </p>
              <button
                onClick={() => onToggle(r)}
                className="text-sm text-neutral-400 px-1"
                title={r.active ? 'Призупинити' : 'Відновити'}
              >
                {r.active ? '⏸' : '▶'}
              </button>
              {/* Видаляє одразу: повернути можна панеллю «Повернути», спільною для всього
                  застосунку. Пара «познач і підтверди» жила тут одна на весь застосунок. */}
              <button
                onClick={() => void onDelete(r.id)}
                className="px-1 text-neutral-300 hover:text-red-500"
                aria-label="Видалити"
              >
                ✕
              </button>
            </li>
          ))}
        </ul>
      )}

    </Screen>
  )
}

/// The one figure this screen is actually opened for: what all of this costs in a month.
/// A list of seven rows on four different rhythms does not add up in anyone's head, and the
/// question behind «підписок забагато» is always the total, never the rows.
///
/// Grouped by the currency each was entered in, and NOT converted: a rate would have to be
/// fetched, and a total in a currency none of the rows are in reads as an app's opinion
/// rather than an answer. Paused rows are left out — they cost nothing while paused.
function MonthlyCost({ items }: { items: RecurringType[] }) {
  const live = items.filter((r) => r.active)
  if (live.length === 0) return null

  const totals = new Map<string, { expense: number; income: number }>()
  for (const r of live) {
    const row = totals.get(r.currencyOriginal) ?? { expense: 0, income: 0 }
    const share = perMonth(r.amountOriginal, r.unit, r.interval)
    if (r.kind === 'Income') row.income += share
    else row.expense += share
    totals.set(r.currencyOriginal, row)
  }

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm">
      <p className="text-sm text-neutral-400">На місяць</p>
      {[...totals.entries()].map(([currency, { expense, income }]) => (
        <p key={currency} className="text-sm tabular-nums">
          {expense > 0 && (
            <span className="text-2xl font-bold">−{money(expense, currency)}</span>
          )}
          {income > 0 && (
            <span className="text-emerald-600 font-semibold">
              {expense > 0 ? ' · ' : ''}+{money(income, currency)}
            </span>
          )}
        </p>
      ))}
      <p className="text-xs text-neutral-400 mt-1">
        {live.length === 1 ? '1 активне' : `${live.length} активних`}
        {items.length > live.length && ` · ${items.length - live.length} призупинено`}
        {' · тижневі й річні перераховані на місяць'}
      </p>
    </div>
  )
}

/// Коли це станеться наступного разу — і чи вже сталось цього періоду. Рядок підписки, що вже
/// списалась, виглядав точно як той, що ще попереду: «кожного 5-го» однаково правдиве і за
/// день до, і за день після, а гроші тим часом уже пішли.
function whenNext(r: RecurringType): string {
  if (!r.active) return 'на паузі'

  const when = r.nextChargeOn
    ? `${dayMonth(r.nextChargeOn)}${untilWords(r.nextChargeOn)}`
    : scheduleSummary(r.unit, r.interval, r.startsOn)

  return r.chargedThisPeriod ? `цього періоду вже пішло · далі ${when}` : when
}

function untilWords(iso: string): string {
  const days = daysUntil(iso)
  if (days <= 0) return ''
  if (days === 1) return ' · завтра'
  return ` · за ${days} дн.`
}
