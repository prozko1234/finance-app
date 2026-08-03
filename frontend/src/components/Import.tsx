import { useState } from 'react'
import type { Category, ImportPreview, ImportResult, ImportRow } from '../types'
import { money, plural } from '../format'
import { groupRows, rowsToCommit, undecidedCount, type ImportGroup } from '../importGroups'
import { Card, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

interface Props {
  categories: Category[]
  onPreview: (file: File) => Promise<ImportPreview>
  onCommit: (rows: ReturnType<typeof rowsToCommit>) => Promise<ImportResult>
  onDone: () => void
  onBack: () => void
}

/// Виписка з банку → транзакції, за три екрани: вибрати файл, звірити, підтвердити.
///
/// Середній крок — увесь сенс. Він показує не 300 рядків, а 25 крамниць: за місяць їх
/// стільки й буває, і рішення приймається раз на крамницю, а не раз на покупку.
export function Import({ categories, onPreview, onCommit, onDone, onBack }: Props) {
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

      <div className="space-y-2">
        {groups.map((g) => (
          <GroupRow
            key={g.key}
            group={g}
            categories={categories}
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

/// Одна крамниця: скільки разів, на скільки грошей, і куди це кладемо.
function GroupRow({ group, categories, onChange }: {
  group: ImportGroup
  categories: Category[]
  onChange: (patch: Partial<ImportGroup>) => void
}) {
  const [open, setOpen] = useState(false)
  const undecided = group.categoryId === null

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

/// Рядки, що вже є в застосунку. Вимкнені за замовчуванням: повторний імпорт того самого
/// місяця не має подвоїти гроші. Але вибір лишається — збіг дня й суми буває й випадковим.
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

/// Найчастіше «імпорт не працює» означає «вивантажив не той файл». Тому приклад — на екрані,
/// а не в голові.
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
