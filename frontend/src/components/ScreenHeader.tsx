/// Every screen behind the menu opens the same way: back arrow, title, nothing else.
/// It was the same seven lines copied seven times, which is how screens quietly drift
/// apart — one of them was the "три різні способи показати налаштування" complaint.
export function ScreenHeader({ title, onBack }: { title: string; onBack: () => void }) {
  return (
    <div className="flex items-center gap-2">
      <button onClick={onBack} className="text-neutral-400 text-2xl leading-none" aria-label="Назад">←</button>
      <h1 className="text-lg font-semibold">{title}</h1>
    </div>
  )
}
