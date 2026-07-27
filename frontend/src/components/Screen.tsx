/// The shape every screen behind the menu shares: back arrow, title, cards, a closing
/// note. It used to be copied per screen, and copies drift — by M23 the four settings
/// screens had four paddings, three ways to save and two ways to show an error. These
/// primitives are the shape itself, so a new screen cannot quietly invent its own.

export function ScreenHeader({ title, onBack }: { title: string; onBack: () => void }) {
  return (
    <div className="flex items-center gap-2">
      <button onClick={onBack} className="text-neutral-400 text-2xl leading-none" aria-label="Назад">←</button>
      <h1 className="text-lg font-semibold">{title}</h1>
    </div>
  )
}

/// Subtitle answers "нащо цей екран", footnote closes with what is easy to worry about.
/// Both optional; when present they always land in the same place and the same size.
export function Screen({ title, onBack, subtitle, footnote, children }: {
  title: string
  onBack: () => void
  subtitle?: string
  footnote?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <div className="space-y-5">
      <ScreenHeader title={title} onBack={onBack} />
      {subtitle && <p className="text-sm text-neutral-500">{subtitle}</p>}
      {children}
      {footnote && <p className="text-xs text-neutral-400 text-center leading-relaxed">{footnote}</p>}
    </div>
  )
}

export function Card({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return (
    <div className={`rounded-2xl bg-white dark:bg-neutral-900 p-5 shadow-sm space-y-3 ${className}`}>
      {children}
    </div>
  )
}

export function SectionTitle({ children }: { children: React.ReactNode }) {
  return <h2 className="text-sm font-medium text-neutral-400">{children}</h2>
}

/// Saving feedback lives in the button label, not in a line that appears next to it:
/// one place to look, and it cannot disagree with the button's own state.
export function PrimaryButton({ onClick, disabled, saved, children }: {
  onClick: () => void
  disabled?: boolean
  saved?: boolean
  children: React.ReactNode
}) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      className="w-full rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 py-2.5 font-medium disabled:opacity-40"
    >
      {saved ? 'Збережено ✓' : children}
    </button>
  )
}

export function FormError({ children }: { children: React.ReactNode }) {
  return children ? <p className="text-sm text-red-600">{children}</p> : null
}

/// A screen still loading keeps its header, so the way back never disappears.
export function CardSkeleton() {
  return <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm animate-pulse h-40" />
}
