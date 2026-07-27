import { useState } from 'react'

export type View = 'home' | 'add' | 'settings' | 'recurring' | 'tax' | 'categories' | 'savings' | 'allocation' | 'dev'

interface Props {
  current: View
  onGo: (v: View) => void
  /// Dev-only screens exist just in a dev build — the API exposes them in Development only.
  showDev: boolean
}

/// Everything reachable from one place. Before this, "налаштування" was a landing page you
/// had to pass through to get anywhere, which put unrelated screens behind a settings label.
const ITEMS: { view: View; label: string; icon: string }[] = [
  { view: 'home', label: 'Головна', icon: '◉' },
  { view: 'savings', label: 'Заощадження', icon: '🐖' },
  { view: 'allocation', label: 'Розподіл бюджету', icon: '🧩' },
  { view: 'recurring', label: 'Підписки й регулярні', icon: '↻' },
  { view: 'categories', label: 'Категорії', icon: '🏷' },
  { view: 'tax', label: 'Податковий профіль', icon: '%' },
  { view: 'settings', label: 'Налаштування', icon: '⚙' },
]

/// One menu, two shapes: a permanent column on desktop, a burger and a slide-over on mobile.
/// Both drive the same list, so a screen can never be reachable in one and lost in the other.
export function Nav({ current, onGo, showDev }: Props) {
  const [open, setOpen] = useState(false)

  const items = showDev ? [...ITEMS, { view: 'dev' as View, label: 'Тестові дані', icon: '🧪' }] : ITEMS

  function go(v: View) {
    onGo(v)
    setOpen(false)
  }

  return (
    <>
      <button
        onClick={() => setOpen(true)}
        className="md:hidden fixed top-5 right-4 z-30 h-10 w-10 rounded-xl bg-white dark:bg-neutral-900 shadow-sm text-xl leading-none"
        aria-label="Меню"
      >
        ☰
      </button>

      {open && (
        <div className="md:hidden fixed inset-0 z-40 flex">
          <div className="flex-1 bg-black/30" onClick={() => setOpen(false)} />
          <nav className="w-64 bg-white dark:bg-neutral-900 p-4 space-y-1 shadow-xl">
            <div className="flex items-center justify-between mb-3 px-2">
              <span className="font-bold">finance</span>
              <button onClick={() => setOpen(false)} className="text-neutral-400 text-xl" aria-label="Закрити">✕</button>
            </div>
            {items.map((i) => <NavItem key={i.view} {...i} current={current} onGo={go} />)}
          </nav>
        </div>
      )}

      <nav className="hidden md:block w-56 shrink-0 space-y-1">
        <p className="text-xl font-bold px-3 pb-3">finance</p>
        {items.map((i) => <NavItem key={i.view} {...i} current={current} onGo={go} />)}
      </nav>
    </>
  )
}

function NavItem({ view, label, icon, current, onGo }: {
  view: View; label: string; icon: string; current: View; onGo: (v: View) => void
}) {
  const active = current === view
  return (
    <button
      onClick={() => onGo(view)}
      aria-current={active ? 'page' : undefined}
      className={`w-full flex items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm ${
        active
          ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 font-medium'
          : 'text-neutral-600 dark:text-neutral-300 hover:bg-neutral-100 dark:hover:bg-neutral-800'
      }`}
    >
      <span className="w-5 text-center">{icon}</span>
      {label}
    </button>
  )
}
