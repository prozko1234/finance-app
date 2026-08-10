import { useState } from 'react'
import type { Category, CategoryKind, SaveCategory } from '../types'
import { Card, FormError, Screen, SectionTitle } from './Screen'
import { EmojiPicker } from './EmojiPicker'

interface Props {
  categories: Category[]
  onCreate: (c: SaveCategory) => Promise<Category>
  onUpdate: (id: number, c: SaveCategory) => Promise<void>
  onDelete: (id: number) => Promise<void>
  onBack: () => void
}

export function Categories({ categories, onCreate, onUpdate, onDelete, onBack }: Props) {
  const [name, setName] = useState('')
  const [icon, setIcon] = useState('')
  const [editingId, setEditingId] = useState<number | null>(null)
  const [editName, setEditName] = useState('')
  const [editIcon, setEditIcon] = useState('')
  // Which list the new category joins. Expense by default, because that is what nearly every
  // category made by hand is — a new source of money is a once-a-year event.
  const [kind, setKind] = useState<CategoryKind>('Expense')
  const [error, setError] = useState<string | null>(null)

  async function run(fn: () => Promise<unknown>) {
    setError(null)
    try {
      await fn()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося')
    }
  }

  return (
    <Screen
      title="Категорії"
      onBack={onBack}
      subtitle="Куди розкладаються витрати й звідки приходять гроші."
      footnote="Видалення не втрачає грошей — транзакції переїжджають у «Інше» свого ж списку."
    >
      <Card>
        <SectionTitle>Нова категорія</SectionTitle>
        {/* Which list it joins, before the name is typed: the two are not interchangeable, and
            a category in the wrong one shows up on a form where it makes no sense. */}
        <div className="flex gap-2">
          {(['Expense', 'Income'] as const).map((k) => (
            <button
              key={k}
              onClick={() => setKind(k)}
              aria-pressed={kind === k}
              className={`flex-1 rounded-xl px-3 py-2 text-sm ${
                kind === k
                  ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                  : 'bg-neutral-100 dark:bg-neutral-800'
              }`}
            >
              {k === 'Expense' ? 'Витрата' : 'Надходження'}
            </button>
          ))}
        </div>
        <div className="flex gap-2">
        <EmojiPicker value={icon} onChange={setIcon} />
        <input
          placeholder="Назва" value={name} onChange={(e) => setName(e.target.value)}
          className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />
        <button
          disabled={!name.trim()}
          onClick={() => run(async () => {
            await onCreate({ name: name.trim(), icon: icon.trim() || null, kind })
            setName(''); setIcon('')
          })}
          className="rounded-xl bg-emerald-600 text-white px-4 text-sm font-medium disabled:opacity-40"
        >
          Додати
        </button>
        </div>
        <FormError>{error}</FormError>
      </Card>

      <CategoryList
        title="На що йдуть гроші"
        rows={categories.filter((c) => c.kind !== 'Income')}
        {...{ editingId, setEditingId, editName, setEditName, editIcon, setEditIcon, run, onUpdate, onDelete }}
      />
      <CategoryList
        title="Звідки приходять"
        rows={categories.filter((c) => c.kind === 'Income')}
        {...{ editingId, setEditingId, editName, setEditName, editIcon, setEditIcon, run, onUpdate, onDelete }}
      />
    </Screen>
  )
}

/// One side of the ledger. Two headed lists rather than one long one: a salary sitting between
/// groceries and rent reads as a mistake, and until income had categories of its own that is
/// exactly what the database contained.
function CategoryList({
  title, rows, editingId, setEditingId, editName, setEditName, editIcon, setEditIcon,
  run, onUpdate, onDelete,
}: {
  title: string
  rows: Category[]
  editingId: number | null
  setEditingId: (id: number | null) => void
  editName: string
  setEditName: (v: string) => void
  editIcon: string
  setEditIcon: (v: string) => void
  run: (fn: () => Promise<unknown>) => Promise<void>
  onUpdate: (id: number, c: SaveCategory) => Promise<void>
  onDelete: (id: number) => Promise<void>
}) {
  if (rows.length === 0) return null

  return (
      <div>
      <SectionTitle>{title}</SectionTitle>
      <ul className="space-y-2 mt-2">
        {rows.map((c) => (
          <li key={c.id} className="rounded-xl bg-white dark:bg-neutral-900 px-4 py-3 shadow-sm">
            {editingId === c.id ? (
              <div className="flex gap-2">
                <EmojiPicker value={editIcon} onChange={setEditIcon} compact />
                <input
                  autoFocus value={editName} onChange={(e) => setEditName(e.target.value)}
                  className="flex-1 rounded-lg bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5 text-sm outline-none"
                />
                <button
                  onClick={() => run(async () => {
                    await onUpdate(c.id, { name: editName.trim(), icon: editIcon.trim() || null })
                    setEditingId(null)
                  })}
                  className="text-sm text-emerald-600 px-2"
                >
                  ✓
                </button>
                <button onClick={() => setEditingId(null)} className="text-sm text-neutral-400 px-2">✕</button>
              </div>
            ) : (
              <div className="flex items-center gap-3">
                <span className="text-xl">{c.icon}</span>
                <span className="flex-1 font-medium">{c.name}</span>
                {c.isSystem ? (
                  <span className="text-xs text-neutral-400">системна</span>
                ) : (
                  <>
                    <button
                      onClick={() => { setEditingId(c.id); setEditName(c.name); setEditIcon(c.icon ?? '') }}
                      className="text-neutral-400 text-sm px-1"
                      aria-label="Перейменувати"
                    >
                      ✎
                    </button>
                    <button
                      onClick={() => run(() => onDelete(c.id))}
                      className="text-neutral-300 hover:text-red-500 px-1"
                      aria-label="Видалити"
                    >
                      ✕
                    </button>
                  </>
                )}
              </div>
            )}
          </li>
        ))}
      </ul>
      </div>
  )
}
