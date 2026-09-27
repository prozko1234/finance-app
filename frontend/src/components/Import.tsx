import { useState } from 'react'
import type { Category, ImportPreview, ImportResult, ImportRow, ImportRowToSave } from '../types'
import { money, plural } from '../format'
import {
  groupRows, rowsAsPlainExpenses, rowsToCommit, subscriptionDecisions, subscriptionRowsToCommit,
  undecidedCount, type ImportGroup, type SubscriptionChoice, type SubscriptionDecision,
} from '../importGroups'
import { dayMonth } from '../format'
import { Card, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

interface Props {
  categories: Category[]
  onPreview: (file: File) => Promise<ImportPreview>
  onCommit: (rows: ImportRowToSave[]) => Promise<ImportResult>
  onDone: () => void
  onBack: () => void
}

/// A bank statement → transactions, in three screens: pick the file, check it, confirm.
///
/// The middle step is the whole point. It shows 25 shops rather than 300 rows: a month has
/// about that many, and a decision is made per shop, not per purchase.
export function Import({ categories, onPreview, onCommit, onDone, onBack }: Props) {
  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [groups, setGroups] = useState<ImportGroup[]>([])
  const [subs, setSubs] = useState<SubscriptionDecision[]>([])
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
      setSubs(subscriptionDecisions(read.rows))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося прочитати файл')
    } finally {
      setBusy(false)
    }
  }

  function update(key: string, patch: Partial<ImportGroup>) {
    setGroups((gs) => gs.map((g) => (g.key === key ? { ...g, ...patch } : g)))
  }

  /// Answering a row "окрема витрата" moves it into the ordinary groups, so it is filed by
  /// shop like anything else — the row does not simply disappear from the import.
  function answer(line: number, choice: SubscriptionChoice) {
    const next = subs.map((d) => (d.row.line === line ? { ...d, choice } : d))
    setSubs(next)
    setGroups(groupRows([
      ...(preview?.rows ?? []).filter((r) => !r.recurring),
      ...rowsAsPlainExpenses(next),
    ]))
  }

  async function commit() {
    setBusy(true)
    setError(null)
    try {
      setResult(await onCommit([
        ...rowsToCommit(groups, duplicates, duplicatesCategory),
        ...subscriptionRowsToCommit(subs, categories[0]?.id ?? null),
      ]))
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
  const unanswered = subs.filter((d) => d.choice === null).length
  const willImport = rowsToCommit(groups, duplicates, duplicatesCategory).length
    + subscriptionRowsToCommit(subs, null).length

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

      {subs.length > 0 && (
        <Subscriptions decisions={subs} unanswered={unanswered} onAnswer={answer} />
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

/// The rows that look like a subscription's charge.
///
/// They are kept out of the shop groups on purpose: the app is ALREADY holding this money, so
/// the question is not where to file the row but whether it is that charge. Importing it as an
/// ordinary expense makes the period pay the same bill twice — and the app's own duplicate
/// check cannot catch it when the price has changed, which is exactly when it matters.
function Subscriptions({ decisions, unanswered, onAnswer }: {
  decisions: SubscriptionDecision[]
  unanswered: number
  onAnswer: (line: number, choice: SubscriptionChoice) => void
}) {
  return (
    <div className="space-y-2">
      <SectionTitle>Схоже на регулярні платежі</SectionTitle>
      <p className="text-sm text-neutral-500">
        Ці гроші апка вже тримає з норми. Скажи, чи це те саме списання — тоді воно
        підтвердиться, а нової витрати не з'явиться.
        {unanswered > 0 && ' Здогадки треба підтвердити: я зіставив їх за датою.'}
      </p>
      {decisions.map((d) => (
        <SubscriptionRow key={d.row.line} decision={d} onAnswer={onAnswer} />
      ))}
    </div>
  )
}

function SubscriptionRow({ decision, onAnswer }: {
  decision: SubscriptionDecision
  onAnswer: (line: number, choice: SubscriptionChoice) => void
}) {
  const { row, choice } = decision
  const m = row.recurring!
  const paid = Math.abs(row.amount)
  const differs = Math.abs(paid - m.ruleAmount) >= 0.01

  const pick = (value: SubscriptionChoice, label: string, primary = false) => (
    <button
      key={value}
      onClick={() => onAnswer(row.line, value)}
      aria-pressed={choice === value}
      className={`rounded-lg px-2.5 py-1.5 text-xs ${
        choice === value
          ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 font-medium'
          : primary
            ? 'bg-neutral-100 dark:bg-neutral-800'
            : 'text-neutral-500'
      }`}
    >
      {label}
    </button>
  )

  return (
    <div className={`rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-2 ${
      choice === null ? 'ring-1 ring-amber-400' : ''
    }`}>
      <div className="flex items-baseline justify-between gap-3">
        <p className="font-medium truncate">
          {m.name}
          {!m.learned && <span className="text-xs text-amber-600"> · здогадка</span>}
        </p>
        <p className="tabular-nums shrink-0">{money(paid, row.currency)}</p>
      </div>

      <p className="text-xs text-neutral-400">
        {dayMonth(row.date)} · {row.merchant}
        {m.chargeOn && ` · в апці ${dayMonth(m.chargeOn)}`}
      </p>

      {/* The pair of amounts, only when they differ — that is the whole reason this row is
          here rather than quietly deduplicated. */}
      {differs && (
        <p className="text-xs">
          <span className="text-neutral-400">в апці </span>
          <span className="tabular-nums">{money(m.ruleAmount, m.ruleCurrency)}</span>
          <span className={paid > m.ruleAmount ? ' text-amber-600' : ' text-emerald-600'}>
            {' '}{paid > m.ruleAmount ? 'подорожчало' : 'подешевшало'}
          </span>
        </p>
      )}

      <div className="flex flex-wrap gap-2">
        {differs && m.canUpdateAmount && pick('reprice', 'Оновити ціну й підтвердити', true)}
        {pick('confirm', differs ? 'Підтвердити, ціну не чіпати' : 'Це воно', true)}
        {pick('expense', 'Окрема витрата')}
      </div>

      {differs && !m.canUpdateAmount && (
        <p className="text-xs text-neutral-400">
          Підписка в {m.ruleCurrency}, а виписка в {row.currency} — ціну звідси взяти не вийде,
          виправ її на екрані підписок.
        </p>
      )}
    </div>
  )
}

/// One shop: how many times, for how much, and where it goes.
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
