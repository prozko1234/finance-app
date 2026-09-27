import { useState } from 'react'
import type { Category, ImportPreview, ImportResult, ImportRow, ImportRowToSave, Recurring } from '../types'
import { money, plural } from '../format'
import {
  groupRows, rowsToCommit, undecidedCount, unansweredGuesses, type ImportGroup,
} from '../importGroups'
import { dayMonth } from '../format'
import { Card, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

interface Props {
  categories: Category[]
  /// The active subscriptions a shop can be linked to. Needed because a price that moved too
  /// far to recognise — Claude going 99,16 → 502,67 — has to be linkable by hand, or the row
  /// imports as an ordinary expense on top of a charge the app is already holding.
  recurring: Recurring[]
  onPreview: (file: File) => Promise<ImportPreview>
  onCommit: (rows: ImportRowToSave[]) => Promise<ImportResult>
  onDone: () => void
  onBack: () => void
}

/// A bank statement → transactions, in three screens: pick the file, check it, confirm.
///
/// The middle step is the whole point. It shows 25 shops rather than 300 rows: a month has
/// about that many, and a decision is made per shop, not per purchase.
export function Import({ categories, recurring, onPreview, onCommit, onDone, onBack }: Props) {
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [groups, setGroups] = useState<ImportGroup[]>([])
  const [duplicatesCategory, setDuplicatesCategory] = useState<number | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const duplicates = preview?.rows.filter((r) => r.duplicateOfId !== null) ?? []

  async function pick(file: File) {
    setBusy(true)
    setError(null)
    try {
      const read = await onPreview(file)
      setPreview(read)
      setGroups(groupRows(read.rows))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося прочитати файл')
    } finally {
      setBusy(false)
    }
  }

  function update(key: string, patch: Partial<ImportGroup>) {
    setGroups((gs) => gs.map((g) => (g.key === key ? { ...g, ...patch } : g)))
  }

  async function commit() {
    setBusy(true)
    setError(null)
    try {
      setResult(await onCommit(rowsToCommit(groups, duplicates, duplicatesCategory)))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося імпортувати')
    } finally {
      setBusy(false)
    }
  }

  if (result) {
    return (
      <Screen title="Імпорт" onBack={onBack}>
        <Card>
          <p className="text-lg font-semibold">
            Додано {result.created} {plural(result.created, 'запис', 'записи', 'записів')}
          </p>
          {/* Counted apart from the created rows, because nothing was written for them: they
              only ticked off a charge the app was already holding. Saying "додано 128" for
              those too would be the screen claiming money moved when it did not. */}
          {(result.confirmed ?? 0) > 0 && (
            <p className="text-sm text-neutral-500">
              Підтверджено {result.confirmed}{' '}
              {plural(result.confirmed!, 'списання', 'списання', 'списань')} підписок
              {(result.repriced ?? 0) > 0 && (
                <> · оновлено ціну в {result.repriced}{' '}
                {plural(result.repriced!, 'підписці', 'підписках', 'підписках')}</>
              )}
            </p>
          )}
          {result.failed > 0 && (
            <p className="text-sm text-amber-600">{result.failed} не вдалося — див. нижче.</p>
          )}
          <ProblemList problems={result.problems} />
          <PrimaryButton onClick={onDone}>На головну</PrimaryButton>
        </Card>
      </Screen>
    )
  }

  if (!preview) {
    return (
      <Screen
        title="Імпорт із банку"
        onBack={onBack}
        subtitle="Виписка у CSV. Формат розпізнається сам."
      >
        <Card>
          <label className="block cursor-pointer rounded-2xl border-2 border-dashed border-neutral-300 dark:border-neutral-700 py-10 text-center">
            <input
              type="file"
              accept=".csv,.txt,text/csv,text/plain"
              className="hidden"
              onChange={(e) => { const f = e.target.files?.[0]; if (f) void pick(f) }}
            />
            <span className="text-3xl">📄</span>
            <p className="mt-2 font-medium">{busy ? 'Читаю…' : 'Обрати файл виписки'}</p>
            <p className="text-xs text-neutral-400 mt-1">CSV з будь-якого банку</p>
          </label>
          <FormError>{error}</FormError>
        </Card>

        <HowToExport />
      </Screen>
    )
  }

  const undecided = undecidedCount(groups)
  const guesses = unansweredGuesses(groups)
  const willImport = rowsToCommit(groups, duplicates, duplicatesCategory).length

  return (
    <Screen
      title="Що я зрозумів"
      onBack={() => { setPreview(null); setGroups([]) }}
      subtitle={
        `${preview.rows.length} ${plural(preview.rows.length, 'рядок', 'рядки', 'рядків')}`
        + ` · ${groups.length} ${plural(groups.length, 'крамниця', 'крамниці', 'крамниць')}`
      }
      footnote={`Прочитано як «${preview.delimiter}» у ${preview.encoding}${preview.headerFound ? ', із заголовком' : ', без заголовка'}.`}
    >
      {undecided > 0 && (
        <Card>
          <p className="text-sm">
            <span className="font-medium">{undecided}</span>{' '}
            {plural(undecided, 'крамниця чекає', 'крамниці чекають', 'крамниць чекають')} на
            категорію — {undecided === 1 ? 'вона вгорі' : 'вони вгорі'}. Решту я вже розклав; якщо десь помилився, виправ, і наступного разу буде правильно.
          </p>
        </Card>
      )}

      {guesses > 0 && (
        <Card>
          <p className="text-sm">
            <span className="font-medium">{guesses}</span>{' '}
            {plural(guesses, 'крамниця схожа', 'крамниці схожі', 'крамниць схожі')} на
            регулярний платіж — я зіставив їх за сумою й датою, тож підтвердь або відхиль. Ці
            гроші апка вже тримає з норми: якщо це те саме списання, нової витрати не буде.
          </p>
        </Card>
      )}

      <div className="space-y-2">
        {groups.map((g) => (
          <GroupRow
            key={g.key}
            group={g}
            categories={categories}
            recurring={recurring}
            onChange={(patch) => update(g.key, patch)}
          />
        ))}
      </div>

      {duplicates.length > 0 && (
        <Duplicates
          rows={duplicates}
          categories={categories}
          categoryId={duplicatesCategory}
          onPick={setDuplicatesCategory}
        />
      )}

      <ProblemList problems={preview.problems} />

      <FormError>{error}</FormError>
      <PrimaryButton onClick={() => void commit()} disabled={busy || willImport === 0}>
        {busy ? 'Імпортую…' : `Імпортувати ${willImport}`}
      </PrimaryButton>
    </Screen>
  )
}

/// One shop: how many times, for how much, and where it goes.
function GroupRow({ group, categories, recurring, onChange }: {
  group: ImportGroup
  categories: Category[]
  recurring: Recurring[]
  onChange: (patch: Partial<ImportGroup>) => void
}) {
  const [open, setOpen] = useState(false)
  const linked = group.recurringId !== null
  // A guess nobody has answered: the app matched on the numbers alone, and acting on that
  // unasked would confirm a bill that never arrived.
  const guess = group.match !== null && !group.match.learned && !linked
  const undecided = (group.categoryId === null && !linked) || guess

  return (
    <div className={`rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-3 ${
      undecided ? 'ring-1 ring-amber-400' : ''
    }`}>
      <div className="flex items-center gap-3">
        <input
          type="checkbox"
          checked={group.include}
          onChange={(e) => onChange({ include: e.target.checked })}
          className="h-5 w-5 shrink-0"
          aria-label={`Імпортувати ${group.merchant}`}
        />
        <button onClick={() => setOpen(!open)} className="flex-1 min-w-0 text-left">
          <p className="font-medium truncate">{group.merchant}</p>
          <p className="text-xs text-neutral-400">
            {group.rows.length} {plural(group.rows.length, 'запис', 'записи', 'записів')} · натисни, щоб побачити
          </p>
        </button>
        <span className={`font-medium tabular-nums shrink-0 ${
          group.total < 0 ? '' : 'text-emerald-600'
        }`}>
          {money(Math.abs(group.total), group.rows[0].currency)}
        </span>
      </div>

      <AsSubscription group={group} recurring={recurring} onChange={onChange} />

      {/* A linked shop needs no category: nothing is created for it — the charge the app
          already wrote is what records the payment. */}
      {!linked && (
        <div className="flex gap-2 flex-wrap">
          {categories.map((c) => (
            <button
              key={c.id}
              onClick={() => onChange({ categoryId: c.id })}
              className={`rounded-xl px-3 py-1.5 text-sm ${
                group.categoryId === c.id
                  ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                  : 'bg-neutral-100 dark:bg-neutral-800'
              }`}
            >
              {c.icon} {c.name}
            </button>
          ))}
        </div>
      )}

      {open && (
        <ul className="space-y-1 pt-1">
          {group.rows.map((r) => (
            <li key={r.line} className="flex gap-2 text-xs text-neutral-500">
              <span className="tabular-nums shrink-0">{r.date}</span>
              <span className="flex-1 min-w-0 truncate">{r.description}</span>
              <span className="tabular-nums shrink-0">{money(Math.abs(r.amount), r.currency)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/// Whether this shop's rows are a subscription's charge rather than new expenses.
///
/// The app is already holding that money, so importing the row as an ordinary expense makes
/// the period pay the same bill twice — and its own duplicate check cannot catch it when the
/// price has changed, which is exactly when it matters.
///
/// Always offered, not only where the server matched something. A price that moved too far to
/// recognise — Claude going 99,16 → 502,67 — is precisely the case that must be linkable, and
/// it is the one the app cannot guess.
function AsSubscription({ group, recurring, onChange }: {
  group: ImportGroup
  recurring: Recurring[]
  onChange: (patch: Partial<ImportGroup>) => void
}) {
  const [picking, setPicking] = useState(false)
  const live = recurring.filter((r) => r.active && r.kind !== 'Income')
  if (live.length === 0) return null

  const m = group.match
  const chosen = live.find((r) => r.id === group.recurringId) ?? null
  const paid = Math.abs(group.total)
  // Compared against the CHARGE, not the rule: the charge is in the currency the statement is
  // in, where a rule kept in euro is not.
  const differs = m?.chargeAmount != null && Math.abs(paid - m.chargeAmount) >= 0.01

  if (chosen === null && m === null && !picking) {
    return (
      <button onClick={() => setPicking(true)} className="text-xs text-neutral-400 underline">
        Це регулярний платіж
      </button>
    )
  }

  return (
    <div className="space-y-2 rounded-xl bg-neutral-50 dark:bg-neutral-800/50 p-3">
      {chosen ? (
        <p className="text-sm">
          <span className="text-neutral-400">Списання </span>
          <span className="font-medium">{chosen.note || chosen.categoryName}</span>
          {m && !m.learned && <span className="text-xs text-amber-600"> · за здогадкою</span>}
        </p>
      ) : (
        <p className="text-sm">
          {m ? (
            <>
              <span className="text-neutral-400">Схоже на </span>
              <span className="font-medium">{m.name}</span>
              <span className="text-xs text-amber-600"> · здогадка</span>
            </>
          ) : 'Який саме платіж?'}
        </p>
      )}

      {/* The two amounts, only when they differ — that is the whole reason this is a question
          rather than a silent deduplication. */}
      {m && differs && (
        <p className="text-xs">
          <span className="text-neutral-400">в апці </span>
          <span className="tabular-nums">{money(m.chargeAmount!, group.rows[0].currency)}</span>
          <span className={paid > m.chargeAmount! ? ' text-amber-600' : ' text-emerald-600'}>
            {' '}{paid > m.chargeAmount! ? 'подорожчало' : 'подешевшало'}
          </span>
          {m.chargeOn && <span className="text-neutral-400"> · {dayMonth(m.chargeOn)}</span>}
        </p>
      )}

      <select
        value={group.recurringId ?? (m && !chosen ? '' : '')}
        onChange={(e) => onChange({
          recurringId: e.target.value === '' ? null : Number(e.target.value),
          updateAmount: false,
        })}
        aria-label="Регулярний платіж"
        className="w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2 text-sm"
      >
        <option value="">— не регулярний платіж —</option>
        {live.map((r) => (
          <option key={r.id} value={r.id}>
            {r.note || r.categoryName} · {money(r.amountOriginal, r.currencyOriginal)}
          </option>
        ))}
      </select>

      {chosen && differs && (
        m?.canUpdateAmount ? (
          <label className="flex items-start gap-2 text-xs">
            <input
              type="checkbox"
              checked={group.updateAmount}
              onChange={(e) => onChange({ updateAmount: e.target.checked })}
              className="mt-0.5"
            />
            <span>Оновити ціну підписки на {money(paid, group.rows[0].currency)}</span>
          </label>
        ) : (
          <p className="text-xs text-neutral-400">
            Підписка в {chosen.currencyOriginal}, а виписка в {group.rows[0].currency} — ціну
            звідси взяти не вийде, виправ її на екрані підписок.
          </p>
        )
      )}
    </div>
  )
}

/// Rows the app already has. Off by default: re-importing the same month must not double the
/// money. The choice stays, though — a matching day and amount can be a coincidence.
function Duplicates({ rows, categories, categoryId, onPick }: {
  rows: ImportRow[]
  categories: Category[]
  categoryId: number | null
  onPick: (id: number | null) => void
}) {
  const [open, setOpen] = useState(false)

  return (
    <Card>
      <SectionTitle>Схоже, це вже є ({rows.length})</SectionTitle>
      <p className="text-sm text-neutral-500">
        Такі самі суми в ті самі дні вже записані — внесені руками або імпортовані раніше.
        За замовчуванням пропускаю.
      </p>
      <button onClick={() => setOpen(!open)} className="text-sm text-neutral-400">
        {open ? 'Згорнути' : 'Показати'}
      </button>
      {open && (
        <ul className="space-y-1">
          {rows.map((r) => (
            <li key={r.line} className="flex gap-2 text-xs text-neutral-500">
              <span className="tabular-nums shrink-0">{r.date}</span>
              <span className="flex-1 min-w-0 truncate">{r.description}</span>
              <span className="tabular-nums shrink-0">{money(Math.abs(r.amount), r.currency)}</span>
            </li>
          ))}
        </ul>
      )}
      <div className="flex gap-2 flex-wrap pt-1">
        <button
          onClick={() => onPick(null)}
          className={`rounded-xl px-3 py-1.5 text-sm ${
            categoryId === null
              ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
              : 'bg-neutral-100 dark:bg-neutral-800'
          }`}
        >
          Пропустити
        </button>
        {categories.map((c) => (
          <button
            key={c.id}
            onClick={() => onPick(c.id)}
            className={`rounded-xl px-3 py-1.5 text-sm ${
              categoryId === c.id
                ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                : 'bg-neutral-100 dark:bg-neutral-800'
            }`}
          >
            {c.icon} {c.name}
          </button>
        ))}
      </div>
    </Card>
  )
}

function ProblemList({ problems }: { problems: { line: number; reason: string; raw: string }[] }) {
  if (problems.length === 0) return null

  return (
    <Card>
      <SectionTitle>Не прочиталось ({problems.length})</SectionTitle>
      <ul className="space-y-1 text-xs text-neutral-500">
        {problems.map((p) => (
          <li key={p.line}>
            <span className="text-neutral-400">рядок {p.line}:</span> {p.reason}
            {p.raw && <span className="block truncate opacity-60">{p.raw}</span>}
          </li>
        ))}
      </ul>
    </Card>
  )
}

/// Most of the time "the import does not work" means "the wrong file was exported". So the
/// example is on the screen rather than in one's head.
function HowToExport() {
  return (
    <Card>
      <SectionTitle>Звідки взяти файл</SectionTitle>
      <ol className="space-y-2 text-sm text-neutral-500 list-decimal pl-4">
        <li>
          <span className="text-neutral-700 dark:text-neutral-200">PKO iPKO:</span> Rachunki →
          Historia → Eksportuj → <span className="font-medium">CSV</span>. Проміжок — від дати
          останнього імпорту.
        </li>
        <li>
          <span className="text-neutral-700 dark:text-neutral-200">mBank:</span> Historia
          operacji → Eksport → CSV.
        </li>
        <li>
          <span className="text-neutral-700 dark:text-neutral-200">Revolut:</span> Account →
          Statement → Excel/CSV.
        </li>
      </ol>
      <p className="text-sm text-neutral-500">
        Формат значення не має — я читаю роздільник, кодування й колонки з самого файлу. Тільки
        не PDF: із нього не витягнути таблицю, потрібен CSV.
      </p>
      <p className="text-sm text-neutral-500">
        Можна вивантажувати щомісяця з перекриттям — те, що вже є, я впізнаю й не додам удруге.
      </p>
    </Card>
  )
}
