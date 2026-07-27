import { useState } from 'react'
import type { Category, SaveCategory } from '../types'
import { ScreenHeader } from './ScreenHeader'

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
    <div className="space-y-5">
      <ScreenHeader title="Категорії" onBack={onBack} />

      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm flex gap-2">
        <input
          placeholder="🍕" value={icon} onChange={(e) => setIcon(e.target.value)}
          className="w-14 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-2 py-2 text-center text-sm outline-none"
        />
        <input
          placeholder="Нова категорія" value={name} onChange={(e) => setName(e.target.value)}
          className="flex-1 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />
        <button
          disabled={!name.trim()}
          onClick={() => run(async () => {
            await onCreate({ name: name.trim(), icon: icon.trim() || null })
            setName(''); setIcon('')
          })}
          className="rounded-xl bg-emerald-600 text-white px-4 text-sm font-medium disabled:opacity-40"
        >
          Додати
        </button>
      </div>

      {error && <p className="text-sm text-red-600 px-1">{error}</p>}

      <ul className="space-y-2">
        {categories.map((c) => (
          <li key={c.id} className="rounded-xl bg-white dark:bg-neutral-900 px-4 py-3 shadow-sm">
            {editingId === c.id ? (
              <div className="flex gap-2">
                <input
                  value={editIcon} onChange={(e) => setEditIcon(e.target.value)}
                  className="w-14 rounded-lg bg-neutral-100 dark:bg-neutral-800 px-2 py-1.5 text-center text-sm outline-none"
                />
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

      <p className="text-xs text-neutral-400 text-center">
        Видалення не втрачає грошей — транзакції переїжджають у «Інше».
      </p>
    </div>
  )
}
