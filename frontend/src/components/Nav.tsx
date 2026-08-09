import { useState } from 'react'

export type View = 'home' | 'add' | 'balance' | 'settings' | 'recurring' | 'tax' | 'categories' | 'savings' | 'debts' | 'allocation' | 'stats' | 'account' | 'import' | 'dev'

interface Props {
  current: View
  onGo: (v: View) => void
  /// The centre of the bottom bar is the main action, not a screen. On desktop a floating
  /// button plays that role, so there is no bar there.
  onAdd: () => void
  /// Dev-only screens exist just in a dev build — the API exposes them in Development only.
  showDev: boolean
  /// Absent when the app runs without an account (local development): a "вийти" that
  /// leads straight back in would be a button that does nothing, and there is no account
  /// screen to show either.
  onLogout?: () => void
}

/// Everything reachable from one place. Before this, "налаштування" was a landing page you
/// had to pass through to get anywhere, which put unrelated screens behind a settings label.
/// Two groups, because a flat list of seven asks you to read all seven: the top group is
/// where money is looked at, the bottom is what you set up once and forget.
type Item = { view: View; label: string; icon: string }

const MONEY: Item[] = [
  { view: 'home', label: 'Головна', icon: '◉' },
  { view: 'balance', label: 'Скільки в мене зараз', icon: '🧮' },
  { view: 'savings', label: 'Банки', icon: '🐖' },
  { view: 'debts', label: 'Борги', icon: '🤝' },
  { view: 'allocation', label: 'Розподіл бюджету', icon: '🧩' },
  { view: 'recurring', label: 'Підписки й регулярні', icon: '↻' },
  { view: 'stats', label: 'Статистика', icon: '📊' },
]

const SETUP: Item[] = [
  { view: 'import', label: 'Імпорт із банку', icon: '📄' },
  { view: 'categories', label: 'Категорії', icon: '🏷' },
  { view: 'tax', label: 'Податковий профіль', icon: '%' },
  { view: 'settings', label: 'Налаштування', icon: '⚙' },
]

/// One menu, two shapes: a permanent column on desktop, a burger and a slide-over on mobile.
/// Both drive the same list, so a screen can never be reachable in one and lost in the other.
export function Nav({ current, onGo, onAdd, showDev, onLogout }: Props) {
  const [open, setOpen] = useState(false)

  // The account screen only exists when there is an account: locally the app has no door,
  // so a page about passwords and sessions would be about nothing.
  const account: Item[] = onLogout ? [{ view: 'account', label: 'Акаунт', icon: '👤' }] : []
  const dev: Item[] = showDev ? [{ view: 'dev', label: 'Тестові дані', icon: '🧪' }] : []
  const setup = [...SETUP, ...account, ...dev]

  function go(v: View) {
    onGo(v)
    setOpen(false)
  }

  return (
    <>
      <BottomBar current={current} onGo={go} onAdd={onAdd} onMore={() => setOpen(true)} />

      {open && (
        <div className="md:hidden fixed inset-0 z-40 flex">
          <div className="flex-1 bg-black/30" onClick={() => setOpen(false)} />
          <nav className="w-64 bg-white dark:bg-neutral-900 p-4 space-y-1 shadow-xl">
            <div className="flex items-center justify-between mb-3 px-2">
              <span className="font-bold">finance</span>
              <button onClick={() => setOpen(false)} className="text-neutral-400 text-xl" aria-label="Закрити">✕</button>
            </div>
            {/* Without what already sits in the bottom bar: a screen reachable by two
                different routes makes both less clear. This is exactly the rest. */}
            <Group items={MONEY.filter((i) => !TABS.some((t) => t.view === i.view))} current={current} onGo={go} />
            <Group items={setup} label="Налаштування" current={current} onGo={go} />
            <LogoutItem onLogout={onLogout} />
          </nav>
        </div>
      )}

      <nav className="hidden md:block w-56 shrink-0 space-y-1">
        <p className="text-xl font-bold px-3 pb-3">finance</p>
        <Group items={MONEY} current={current} onGo={go} />
        <Group items={setup} label="Налаштування" current={current} onGo={go} />
      </nav>
    </>
  )
}

/// The bottom bar is what a thumb reaches for. Four tabs and the action in the middle: more
/// than that does not fit across the screen with a readable label, and without a label an icon
/// becomes a riddle.
///
/// "Ще" opens the same drawer the burger used to — no screen disappeared, the four most
/// frequent ones simply stopped being two taps away.
const TABS: Item[] = [
  { view: 'home', label: 'Головна', icon: '◉' },
  { view: 'savings', label: 'Банки', icon: '🐖' },
  { view: 'stats', label: 'Статистика', icon: '📊' },
]

function BottomBar({ current, onGo, onAdd, onMore }: {
  current: View
  onGo: (v: View) => void
  onAdd: () => void
  onMore: () => void
}) {
  // Screens that are not tabs light up "Ще" — otherwise the bar shows "Головна" as active
  // while the tax profile is on screen, and lies about where you are.
  const inTabs = TABS.some((t) => t.view === current)

  return (
    <nav
      // The bar is fixed, so it needs its own home-indicator inset: padding on <body> does
      // not reach fixed elements.
      style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}
      className="md:hidden fixed bottom-0 inset-x-0 z-30 flex items-stretch border-t border-neutral-200 dark:border-neutral-800 bg-white/95 dark:bg-neutral-900/95 backdrop-blur"
      aria-label="Основне меню"
    >
      <Tab {...TABS[0]} active={current === TABS[0].view} onGo={onGo} />
      <Tab {...TABS[1]} active={current === TABS[1].view} onGo={onGo} />

      <button
        onClick={onAdd}
        aria-label="Додати транзакцію"
        className="flex-1 flex justify-center items-start pt-1"
      >
        <span className="h-12 w-12 -mt-5 rounded-full bg-emerald-600 text-white text-2xl shadow-lg flex items-center justify-center">
          +
        </span>
      </button>

      <Tab {...TABS[2]} active={current === TABS[2].view} onGo={onGo} />
      <Tab
        view="settings" label="Ще" icon="☰"
        active={!inTabs && current !== 'add'}
        onGo={onMore}
      />
    </nav>
  )
}

function Tab({ view, label, icon, active, onGo }: {
  view: View; label: string; icon: string; active: boolean; onGo: (v: View) => void
}) {
  return (
    <button
      onClick={() => onGo(view)}
      aria-current={active ? 'page' : undefined}
      className={`flex-1 flex flex-col items-center gap-0.5 py-2 text-[10px] ${
        active ? 'text-emerald-600 font-medium' : 'text-neutral-400'
      }`}
    >
      <span className="text-lg leading-none">{icon}</span>
      {label}
    </button>
  )
}

function LogoutItem({ onLogout }: { onLogout?: () => void }) {
  if (!onLogout) return null
  return (
    <button
      onClick={onLogout}
      className="w-full flex items-center gap-3 rounded-xl px-3 py-2.5 text-left text-sm text-neutral-500 hover:bg-neutral-100 dark:hover:bg-neutral-800"
    >
      <span className="w-5 text-center">⎋</span>
      Вийти
    </button>
  )
}

function Group({ items, label, current, onGo }: {
  items: Item[]; label?: string; current: View; onGo: (v: View) => void
}) {
  return (
    <div className="space-y-1 pb-3">
      {label && <p className="px-3 pt-2 pb-1 text-xs uppercase tracking-wide text-neutral-400">{label}</p>}
      {items.map((i) => <NavItem key={i.view} {...i} current={current} onGo={onGo} />)}
    </div>
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
