import { useEffect, useRef, useState } from 'react'

/// Curated set instead of the full Unicode emoji list: a category icon only ever needs
/// "what kind of spending is this", and a short grid is faster to scan than a searchable
/// picker — plus it costs no dependency and no emoji data bundle.
const EMOJIS = [
  '🍕', '🍔', '🍜', '☕', '🍺', '🛒', '🥦', '🍎',
  '🏠', '💡', '🔥', '💧', '📶', '🧺', '🪑', '🧹',
  '🚗', '⛽', '🚌', '🚕', '🚲', '✈️', '🛵', '🅿️',
  '💊', '🏥', '🦷', '💪', '💇', '🧴', '👕', '👟',
  '🎬', '🎮', '🎧', '📚', '🎁', '🎉', '🐈', '🐕',
  '💻', '📱', '🔧', '📄', '🎓', '💳', '💸', '🏦',
]

interface Props {
  value: string
  onChange: (icon: string) => void
  compact?: boolean
}

export function EmojiPicker({ value, onChange, compact }: Props) {
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    function onPointerDown(e: PointerEvent) {
      if (!root.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [open])

  return (
    <div className="relative" ref={root}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-label="Обрати емодзі"
        className={`w-14 rounded-xl bg-neutral-100 dark:bg-neutral-800 text-center outline-none ${
          compact ? 'py-1.5 rounded-lg' : 'py-2'
        }`}
      >
        {value ? <span className="text-lg">{value}</span> : <span className="text-neutral-400 text-sm">🙂</span>}
      </button>

      {open && (
        <div className="absolute z-20 mt-1 w-64 rounded-xl bg-white dark:bg-neutral-900 p-2 shadow-lg ring-1 ring-black/5 dark:ring-white/10">
          <div className="grid grid-cols-8 gap-1">
            {EMOJIS.map((e) => (
              <button
                key={e}
                type="button"
                onClick={() => { onChange(e); setOpen(false) }}
                className={`rounded-lg py-1 text-lg hover:bg-neutral-100 dark:hover:bg-neutral-800 ${
                  e === value ? 'bg-neutral-100 dark:bg-neutral-800' : ''
                }`}
              >
                {e}
              </button>
            ))}
          </div>
          {value && (
            <button
              type="button"
              onClick={() => { onChange(''); setOpen(false) }}
              className="mt-1 w-full rounded-lg py-1 text-xs text-neutral-400 hover:bg-neutral-100 dark:hover:bg-neutral-800"
            >
              Без емодзі
            </button>
          )}
        </div>
      )}
    </div>
  )
}
