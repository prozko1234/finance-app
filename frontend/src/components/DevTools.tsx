import { useState } from 'react'
import { useDevData } from '../hooks'

/// Local testing helpers, so a flow can be re-run from a known state without
/// hand-deleting rows. Only rendered when the app runs against a dev server;
/// the API refuses to expose these endpoints outside Development.
export function DevTools({ onBack }: { onBack: () => void }) {
  const { reset, seed } = useDevData()
  const [confirming, setConfirming] = useState<'reset' | 'seed' | null>(null)
  const [done, setDone] = useState<string | null>(null)

  const busy = reset.isPending || seed.isPending

  async function run(which: 'reset' | 'seed') {
    setConfirming(null)
    setDone(null)
    const r = which === 'reset' ? await reset.mutateAsync() : await seed.mutateAsync()
    setDone(r.message)
  }

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onBack} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">Тестові дані</h1>
      </div>

      <p className="text-sm text-neutral-500">
        Тільки для розробки. Ці кнопки міняють базу без відновлення.
      </p>

      {done && <p className="rounded-xl bg-emerald-50 dark:bg-emerald-950 px-3 py-2 text-sm text-emerald-700 dark:text-emerald-300">{done}</p>}

      <Action
        title="Приклад місяця"
        description="Стирає все і створює: профіль ryczałt 12% + VAT, одну фактуру на 24 600 брутто, п'ять витрат, дві підписки, план відкладання 10%."
        confirmLabel="Так, замінити всі дані"
        busy={busy}
        confirming={confirming === 'seed'}
        onAsk={() => setConfirming('seed')}
        onCancel={() => setConfirming(null)}
        onConfirm={() => run('seed')}
      />

      <Action
        title="Очистити все"
        description="Стирає транзакції, підписки, відкладення, податковий профіль і бюджет. Категорії лишаються."
        confirmLabel="Так, стерти все"
        danger
        busy={busy}
        confirming={confirming === 'reset'}
        onAsk={() => setConfirming('reset')}
        onCancel={() => setConfirming(null)}
        onConfirm={() => run('reset')}
      />
    </div>
  )
}

function Action({ title, description, confirmLabel, danger, busy, confirming, onAsk, onCancel, onConfirm }: {
  title: string
  description: string
  confirmLabel: string
  danger?: boolean
  busy: boolean
  confirming: boolean
  onAsk: () => void
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-4 shadow-sm space-y-2">
      <h2 className="font-medium">{title}</h2>
      <p className="text-xs text-neutral-400">{description}</p>

      {confirming ? (
        <div className="flex gap-2 pt-1">
          <button
            disabled={busy}
            onClick={onConfirm}
            className={`flex-1 rounded-xl px-3 py-2.5 font-medium text-white disabled:opacity-40 ${danger ? 'bg-red-600' : 'bg-neutral-900 dark:bg-white dark:text-neutral-900'}`}
          >
            {busy ? 'Роблю…' : confirmLabel}
          </button>
          <button onClick={onCancel} className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-4 py-2.5">
            Ні
          </button>
        </div>
      ) : (
        <button
          onClick={onAsk}
          className={`w-full rounded-xl px-3 py-2.5 font-medium ${danger ? 'bg-red-50 dark:bg-red-950 text-red-700 dark:text-red-300' : 'bg-neutral-100 dark:bg-neutral-800'}`}
        >
          {title}
        </button>
      )}
    </div>
  )
}
