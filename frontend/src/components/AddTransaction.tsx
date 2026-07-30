import { useState } from 'react'
import type {
  Category, EnvelopeSummary, IncomePreview, SaveCategory, SaveIncome, SaveRecurring, SaveTransaction, Transaction,
} from '../types'
import { BASE_CURRENCY, CURRENCIES, shiftIso, todayIso } from '../types'
import { money } from '../format'
import { useIncomePreview, useSaveSavingsPlan, useSettings, useTaxProfile } from '../hooks'
import { readIncomeSources, readLastUsed, rememberIncomeSource, writeLastUsed } from '../lastUsed'

interface Props {
  categories: Category[]
  /// Банки з балансом — з них можна заплатити напряму. Порожній список = вибору немає,
  /// і питання «звідки гроші» не показується взагалі.
  envelopes: EnvelopeSummary[]
  onSave: (tx: SaveTransaction) => Promise<void>
  onSaveIncome: (income: SaveIncome) => Promise<void>
  onSaveRecurring: (r: SaveRecurring) => Promise<void>
  onCreateCategory: (c: SaveCategory) => Promise<Category>
  onCancel: () => void
  /// When set, the form edits this transaction instead of creating a new one.
  editing?: Transaction | null
  /// Preselected category from a quick-category tap — only the amount is left to type.
  presetCategoryId?: number | null
  /// З чого відкрити форму. Головна просить дохід — і форма має відкритись на ньому.
  initialKind?: 'expense' | 'income'
}

/// One form, three things you can add — so everything is one tap from "+".
type Kind = 'expense' | 'income' | 'subscription'
const KIND_LABEL: Record<Kind, string> = {
  expense: '↑ Витрата', income: '↓ Дохід', subscription: '↻ Підписка',
}
const KIND_TITLE: Record<Kind, string> = {
  expense: 'Нова транзакція', income: 'Новий дохід', subscription: 'Нова підписка',
}


export function AddTransaction({
  categories, envelopes, onSave, onSaveIncome, onSaveRecurring, onCreateCategory, onCancel,
  editing, presetCategoryId, initialKind = 'expense',
}: Props) {
  const [newCatOpen, setNewCatOpen] = useState(false)
  const [newCatName, setNewCatName] = useState('')
  const [newCatIcon, setNewCatIcon] = useState('')
  const last = readLastUsed()
  const [kind, setKind] = useState<Kind>(initialKind)
  const isIncome = kind === 'income'
  const isSubscription = kind === 'subscription'
  const [amount, setAmount] = useState(editing ? String(editing.amountOriginal) : '')
  // Open pre-filled with what was used last time — fewer taps per entry.
  const [currency, setCurrency] = useState(editing?.currencyOriginal ?? last.currency ?? 'PLN')
  const [categoryId, setCategoryId] = useState<number | null>(
    editing?.categoryId
      ?? categories.find((c) => c.id === presetCategoryId)?.id
      ?? categories.find((c) => c.id === last.categoryId)?.id
      ?? categories[0]?.id
      ?? null,
  )
  const [date, setDate] = useState(editing?.date ?? todayIso())
  const [dayOfMonth, setDayOfMonth] = useState('1')
  const [incomeRepeats, setIncomeRepeats] = useState(false)
  const [envelopeId, setEnvelopeId] = useState<number | null>(editing?.envelopeId ?? null)
  const [includesVat, setIncludesVat] = useState(true)
  const [note, setNote] = useState(editing?.note ?? '')
  // Remembered once per mount: the list only changes on save, and the form closes then.
  const [incomeSources] = useState(readIncomeSources)
  // The income form follows the tax profile: on "просто гроші" there is no VAT to split off,
  // so asking brutto/netto would be a question with no meaning behind it.
  const vatApplies = useTaxProfile().data?.regime === 'Ryczalt'
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amountNum = Number(amount.replace(',', '.'))
  const dayNum = Number(dayOfMonth)
  const repeats = isSubscription || (isIncome && incomeRepeats)
  const valid = amountNum > 0
    && (isIncome || categoryId !== null)
    && (!isSubscription || categoryId !== null)
    && (!repeats || (dayNum >= 1 && dayNum <= 31))

  async function submit() {
    if (!valid) return
    setSaving(true)
    setError(null)
    try {
      if (repeats) {
        await onSaveRecurring({
          amount: amountNum,
          currency,
          // Income rows still need a category to hang off; the first one is the income
          // category the manual income flow already uses.
          categoryId: categoryId ?? categories[0].id,
          dayOfMonth: dayNum,
          note: note.trim() || null,
          active: true,
          kind: isIncome ? 'Income' : 'Expense',
          amountIncludesVat: includesVat,
        })
        if (isIncome) rememberIncomeSource(note)
        else writeLastUsed({ categoryId: categoryId!, currency })
      } else if (isIncome) {
        await onSaveIncome({
          amount: amountNum,
          amountIncludesVat: includesVat,
          currency,
          date,
          note: note.trim() || null,
        })
        rememberIncomeSource(note)
      } else {
        await onSave({
          amount: amountNum,
          currency,
          categoryId: categoryId!,
          envelopeId,
          frequency: 'OneOff',
          date,
          note: note.trim() || null,
        })
        writeLastUsed({ categoryId: categoryId!, currency })
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onCancel} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">
          {editing ? 'Редагувати' : KIND_TITLE[kind]}
        </h1>
      </div>

      {/* What is being added — not offered while editing an existing row */}
      <div className={`flex gap-2 ${editing ? 'hidden' : ''}`}>
        {(['expense', 'income', 'subscription'] as const).map((k) => (
          <button
            key={k}
            onClick={() => {
              setKind(k)
              // The income note is a client name; it has no business in an expense.
              if (k === 'income' && !note.trim()) setNote(incomeSources[0] ?? '')
              if (k !== 'income' && incomeSources.includes(note.trim())) setNote('')
            }}
            className={`flex-1 rounded-xl px-2 py-2.5 text-sm font-medium ${
              kind === k
                ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                : 'bg-neutral-100 dark:bg-neutral-800'
            }`}
          >
            {KIND_LABEL[k]}
          </button>
        ))}
      </div>

      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-5 shadow-sm space-y-4">
        {/* Amount + currency */}
        <div className="flex gap-2">
          <input
            type="text"
            inputMode="decimal"
            autoFocus
            placeholder="0"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className="flex-1 text-4xl font-bold tabular-nums bg-transparent outline-none w-full"
          />
          <select
            value={currency}
            onChange={(e) => setCurrency(e.target.value)}
            className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 font-medium"
          >
            {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        {isIncome ? (
          <>
            {vatApplies && (
            <div>
              <label className="text-xs text-neutral-400">Що прийшло</label>
              <div className="mt-1 flex gap-2">
                {[true, false].map((v) => (
                  <button
                    key={String(v)}
                    onClick={() => setIncludesVat(v)}
                    className={`flex-1 rounded-xl px-3 py-2 text-sm ${
                      includesVat === v
                        ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                        : 'bg-neutral-100 dark:bg-neutral-800'
                    }`}
                  >
                    {v ? 'з VAT (brutto)' : 'без VAT (netto)'}
                  </button>
                ))}
              </div>
            </div>
            )}
            <label className="flex items-center gap-2 text-sm text-neutral-500">
              <input
                type="checkbox"
                checked={incomeRepeats}
                onChange={(e) => setIncomeRepeats(e.target.checked)}
              />
              Приходить щомісяця (стабільний дохід)
            </label>

            {incomeRepeats
              ? (
                <p className="text-xs text-neutral-400">
                  Зарахується автоматично кожного місяця. Бюджет перерахується сам —
                  вписувати щомісяця не треба.
                </p>
              )
              : <IncomePreviewBlock amount={amountNum} includesVat={includesVat} currency={currency} />}
          </>
        ) : (
          <>
            {/* Category */}
            <div>
              <label className="text-xs text-neutral-400">Категорія</label>
              <div className="mt-1 flex flex-wrap gap-2">
                {categories.map((c) => (
                  <button
                    key={c.id}
                    onClick={() => setCategoryId(c.id)}
                    className={`rounded-xl px-3 py-2 text-sm ${
                      categoryId === c.id
                        ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                        : 'bg-neutral-100 dark:bg-neutral-800'
                    }`}
                  >
                    {c.icon} {c.name}
                  </button>
                ))}
                <button
                  onClick={() => setNewCatOpen((o) => !o)}
                  className="rounded-xl px-3 py-2 text-sm border border-dashed border-neutral-300 dark:border-neutral-700 text-neutral-500"
                >
                  + Нова
                </button>
              </div>

              {newCatOpen && (
                <div className="mt-2 flex gap-2">
                  <input
                    autoFocus
                    placeholder="🍕"
                    value={newCatIcon}
                    onChange={(e) => setNewCatIcon(e.target.value)}
                    className="w-14 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-2 py-2 text-center text-sm outline-none"
                  />
                  <input
                    placeholder="Назва категорії"
                    value={newCatName}
                    onChange={(e) => setNewCatName(e.target.value)}
                    className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
                  />
                  <button
                    disabled={!newCatName.trim()}
                    onClick={async () => {
                      try {
                        const created = await onCreateCategory({
                          name: newCatName.trim(),
                          icon: newCatIcon.trim() || null,
                        })
                        setCategoryId(created.id)   // pick it right away — zero extra taps
                        setNewCatName('')
                        setNewCatIcon('')
                        setNewCatOpen(false)
                      } catch (e) {
                        setError(e instanceof Error ? e.message : 'Не вдалося створити')
                      }
                    }}
                    className="rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-4 text-sm font-medium disabled:opacity-40"
                  >
                    OK
                  </button>
                </div>
              )}
            </div>

            {/* Звідки гроші — замість «треба/варто/хочу», яке нічого не міняло на жодному
                екрані. Дефолт («З основних») уже вибраний, тож звичайна витрата
                вводиться так само швидко, як і раніше. Для підписки питання не стоїть. */}
            {!isSubscription && envelopes.length > 0 && (
              <div>
                <label className="text-xs text-neutral-400">Звідки гроші</label>
                <div className="mt-1 flex gap-2 flex-wrap">
                  <SourceButton
                    label="З основних"
                    active={envelopeId === null}
                    onClick={() => setEnvelopeId(null)}
                  />
                  {envelopes.map((e) => (
                    <SourceButton
                      key={e.id}
                      label={e.name}
                      active={envelopeId === e.id}
                      onClick={() => setEnvelopeId(e.id)}
                    />
                  ))}
                </div>
                {envelopeId !== null && (
                  <p className="mt-1 text-xs text-neutral-400">
                    Зменшить банку, а не денну норму — ці гроші вже відкладені.
                  </p>
                )}
              </div>
            )}
          </>
        )}

        {repeats && (
          <div className="flex items-center gap-2 text-sm">
            <span className="text-neutral-500">кожного</span>
            <input
              inputMode="numeric"
              value={dayOfMonth}
              onChange={(e) => setDayOfMonth(e.target.value)}
              className="w-14 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-2 py-1 text-center"
            />
            <span className="text-neutral-500">числа</span>
          </div>
        )}

        {/* Коли. Дохід теж: зарплата, яку вписали через три дні, має лягти у свій період,
            а не в той, у якому її нарешті ввели. API поле приймав давно — форма ні. */}
        {!isSubscription && !repeats && (
          <div>
            <label className="text-xs text-neutral-400">Коли</label>
            <div className="mt-1 flex gap-2 items-center">
              {[
                { label: 'Сьогодні', value: todayIso() },
                { label: 'Вчора', value: shiftIso(todayIso(), -1) },
              ].map((o) => (
                <button
                  key={o.label}
                  onClick={() => setDate(o.value)}
                  className={`rounded-xl px-3 py-2 text-sm ${
                    date === o.value
                      ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                      : 'bg-neutral-100 dark:bg-neutral-800'
                  }`}
                >
                  {o.label}
                </button>
              ))}
              <input
                type="date"
                value={date}
                max={todayIso()}
                onChange={(e) => setDate(e.target.value)}
                className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
              />
            </div>
          </div>
        )}

        {/* Note */}
        <div>
          <input
            type="text"
            placeholder={
              isIncome ? 'Від кого / за що'
                : isSubscription ? 'Назва (Netflix, оренда…)'
                  : "Нотатка (необов'язково)"
            }
            value={note}
            onChange={(e) => setNote(e.target.value)}
            className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
          />
          {isIncome && incomeSources.length > 1 && (
            <div className="mt-2 flex flex-wrap gap-2">
              {incomeSources.map((s) => (
                <button
                  key={s}
                  onClick={() => setNote(s)}
                  className={`rounded-lg px-2.5 py-1 text-xs ${
                    note.trim() === s
                      ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                      : 'bg-neutral-100 dark:bg-neutral-800 text-neutral-500'
                  }`}
                >
                  {s}
                </button>
              ))}
            </div>
          )}
        </div>

        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>

      <button
        onClick={submit}
        disabled={!valid || saving}
        className="w-full rounded-2xl bg-emerald-600 text-white py-4 font-semibold disabled:opacity-40"
      >
        {saving
          ? 'Зберігаю…'
          : editing ? 'Зберегти зміни'
            : isSubscription ? 'Додати підписку'
              : repeats ? 'Додати регулярний дохід'
                : 'Зберегти'}
      </button>
    </div>
  )
}

/// The point of M13: the form answers "what does this actually give me" while you type,
/// so there is no reason to reach for an external calculator.
/// Shows a MONTHLY delta — a second invoice adds more than the first, because ZUS and
/// health are already covered. A per-invoice figure here would contradict the home screen.
function IncomePreviewBlock({ amount, includesVat, currency }: {
  amount: number; includesVat: boolean; currency: string
}) {
  const isBase = currency === BASE_CURRENCY
  const { data, isFetching } = useIncomePreview(amount, includesVat, isBase)

  if (!isBase) {
    return (
      <p className="text-xs text-neutral-400">
        Розклад податків рахується в {BASE_CURRENCY}. Дохід у {currency} перерахується
        за курсом при збереженні.
      </p>
    )
  }

  if (!data) {
    return (
      <p className="text-xs text-neutral-400">
        {amount > 0 ? 'Рахую…' : 'Введи суму — покажу, скільки з неї твоє.'}
      </p>
    )
  }

  return (
    <div className={`rounded-xl bg-neutral-50 dark:bg-neutral-800/60 p-3 space-y-2 ${isFetching ? 'opacity-60' : ''}`}>
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-xs text-neutral-400">Твоє з цієї суми</span>
        <span className="text-2xl font-bold tabular-nums text-emerald-600">
          + {money(data.budgetDelta, data.currency)}
        </span>
      </div>

      {/* Nothing is withheld on "просто гроші", so the breakdown would just repeat the amount. */}
      <div className="text-xs text-neutral-400 space-y-0.5">
        {data.invoiceVat > 0 && (
          <>
            <div className="flex justify-between gap-3">
              <span>Прийде на рахунок</span>
              <span className="tabular-nums">{money(data.invoiceGross, data.currency)}</span>
            </div>
            <div className="flex justify-between gap-3">
              <span>VAT</span>
              <span className="tabular-nums">− {money(data.invoiceVat, data.currency)}</span>
            </div>
          </>
        )}
        {data.monthAfter.setAside > 0 && !data.isFirstIncomeThisMonth && (
          <div className="flex justify-between gap-3">
            <span>ZUS і здоровотна вже покриті цього місяця</span>
            <span className="tabular-nums">−</span>
          </div>
        )}
      </div>

      <div className="flex justify-between gap-3 border-t border-neutral-200 dark:border-neutral-700 pt-1.5 text-xs">
        <span className="text-neutral-400">Бюджет місяця стане</span>
        <span className="tabular-nums font-medium">{money(data.budgetAfter, data.currency)}</span>
      </div>

      <TaxCurrencyNote />

      <SavingsRow preview={data} />
    </div>
  )
}

/// M17 / story 2: where the money goes has to be visible — and changeable — without
/// leaving the form. The separate savings screen stays for the history of movements.
function SavingsRow({ preview }: { preview: IncomePreview }) {
  const savePlan = useSaveSavingsPlan()
  const [editing, setEditing] = useState(false)
  // Ціль диктує схема — тоді редактор плану тут був би кнопкою, яка нічого не робить.
  // Раніше форма ще й показувала суму з плану, якої додаток відкладати не збирався.
  const fromScheme = preview.savingsFromScheme
  const [mode, setMode] = useState<'Fixed' | 'Percent'>(preview.savingsMode)
  const [value, setValue] = useState(preview.savingsValue > 0 ? String(preview.savingsValue) : '')

  const num = Number(value.replace(',', '.'))
  const valid = num >= 0 && (mode !== 'Percent' || num <= 100)

  async function save() {
    if (!valid || savePlan.isPending) return
    await savePlan.mutateAsync({ mode, value: num, active: num > 0 })
    setEditing(false)
  }

  if (fromScheme) {
    return (
      <div className="flex justify-between gap-3 border-t border-neutral-200 dark:border-neutral-700 pt-1.5 text-xs">
        <span className="text-neutral-400">У банки за схемою «{fromScheme}»</span>
        <span className="tabular-nums font-medium">
          {money(preview.savingsGoalAfter, preview.currency)}
        </span>
      </div>
    )
  }

  if (!editing) {
    const share = preview.savingsActive && preview.savingsMode === 'Percent' && preview.savingsValue > 0
      ? ` (${preview.savingsValue}%)`
      : ''
    return (
      <button
        onClick={() => setEditing(true)}
        className="w-full flex justify-between gap-3 border-t border-neutral-200 dark:border-neutral-700 pt-1.5 text-xs text-left"
      >
        <span className="text-neutral-400">
          {preview.savingsGoalAfter > 0 ? `У заощадження піде${share}` : 'У заощадження нічого не піде'}
          <span className="text-neutral-300 dark:text-neutral-600"> · змінити</span>
        </span>
        <span className="tabular-nums font-medium">
          {money(preview.savingsGoalAfter, preview.currency)}
        </span>
      </button>
    )
  }

  return (
    <div className="border-t border-neutral-200 dark:border-neutral-700 pt-2 space-y-2">
      <div className="flex gap-2">
        {(['Percent', 'Fixed'] as const).map((m) => (
          <button
            key={m}
            onClick={() => setMode(m)}
            className={`flex-1 rounded-lg px-2 py-1.5 text-xs ${
              mode === m
                ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                : 'bg-neutral-100 dark:bg-neutral-700'
            }`}
          >
            {m === 'Percent' ? '% від бюджету' : 'Сума'}
          </button>
        ))}
      </div>
      <div className="flex items-center gap-2">
        <input
          type="text"
          inputMode="decimal"
          autoFocus
          placeholder="0"
          value={value}
          onChange={(e) => setValue(e.target.value)}
          className="flex-1 text-xl font-bold tabular-nums bg-transparent outline-none"
        />
        <span className="text-xs text-neutral-400">{mode === 'Percent' ? '%' : preview.currency}</span>
        <button
          onClick={save}
          disabled={!valid || savePlan.isPending}
          className="rounded-lg bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 px-3 py-1.5 text-xs font-medium disabled:opacity-40"
        >
          OK
        </button>
      </div>
      <p className="text-xs text-neutral-400">
        Відкладене ховається з «Можна витратити сьогодні» — але лишається твоїм, зняти можна будь-коли.
      </p>
    </div>
  )
}

/// Податковий рушій польський: ставки, ZUS і здоровотна визначені в злотих, і саме ці
/// цифри побачить книгова. Тому розклад лишається в PLN, навіть коли решта застосунку
/// читається в іншій валюті — мовчазна конвертація дала б число, якого немає в жодному
/// документі.
function TaxCurrencyNote() {
  const { data: settings } = useSettings()
  if (!settings?.taxesInBaseCurrency) return null

  return (
    <p className="text-xs text-amber-600 dark:text-amber-400 leading-relaxed">
      Розклад податків — у {settings.baseCurrency}: ставки й внески рахуються в злотих,
      це ті самі цифри, що в книгової. Решта застосунку показується
      в {settings.displayCurrency}.
    </p>
  )
}

/// Одна кнопка вибору джерела. Виглядає як решта вибору у формі, щоб питання читалось
/// як «звідки», а не як ще одна налаштовувана штука.
function SourceButton({ label, active, onClick }: {
  label: string
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      onClick={onClick}
      aria-pressed={active}
      className={`rounded-xl px-3 py-2 text-sm ${
        active
          ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
          : 'bg-neutral-100 dark:bg-neutral-800'
      }`}
    >
      {label}
    </button>
  )
}
