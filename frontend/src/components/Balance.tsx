import { useState } from 'react'
import type { OpeningBalance, SaveOpeningBalance } from '../types'
import { CURRENCIES, todayIso } from '../types'
import { dayMonth, money } from '../format'
import { Card, CardSkeleton, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

interface Props {
  data: OpeningBalance | null
  /// Валюта читання — дефолт для нового підрахунку.
  currency: string
  onSet: (b: SaveOpeningBalance) => Promise<void>
  onClear: () => Promise<void>
  onBack: () => void
}

/// «Скільки в мене зараз є» — та сама цифра, що на першому екрані банку.
///
/// Механізм існував із самого початку, але ввести його можна було
/// ЛИШЕ в онбордингу, тобто один раз у житті застосунку. А він перебиває бюджет із доходу,
/// зсуває вікно розрахунку і ставить план відкладень на паузу до наступної зарплати — тобто
/// керує головною цифрою. Помилкову суму не було чим виправити: вона діяла до кінця періоду.
/// Тепер це звичайний екран: порахувати заново, побачити, що діє, і прибрати.
export function Balance({ data, currency, onSet, onClear, onBack }: Props) {
  return (
    <Screen
      title="Скільки в мене зараз"
      onBack={onBack}
      subtitle="Коли на рахунку вже не те, що прийшло — порахуй залишок, і денна норма піде від нього."
      footnote="Це не витрата й не дохід: застосунок просто вірить тобі, що зараз є стільки. Наступного періоду бюджет знову рахується з доходу."
    >
      {data === null ? <CardSkeleton /> : (
        <>
          <Current data={data} onClear={onClear} />
          <CountForm currency={currency} onSet={onSet} />
        </>
      )}
    </Screen>
  )
}

/// Що діє прямо зараз. `appliesNow` API повертав давно, але ніде не показувався — і
/// зрозуміти, чому норма менша за очікувану, було нізвідки.
function Current({ data, onClear }: { data: OpeningBalance; onClear: () => Promise<void> }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!data.isSet || data.amount === null) {
    return (
      <Card>
        <SectionTitle>Зараз не задано</SectionTitle>
        <p className="text-sm text-neutral-500">
          Бюджет рахується з доходу за період — так і має бути, поки все прийшло цього періоду.
        </p>
      </Card>
    )
  }

  async function clear() {
    setBusy(true)
    setError(null)
    try {
      await onClear()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося прибрати')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>{data.appliesNow ? 'Діє зараз' : 'Уже не діє'}</SectionTitle>
      <p className="text-3xl font-bold tabular-nums">{money(data.amount, data.currency)}</p>
      <p className="text-sm text-neutral-500">
        {data.appliesNow
          ? `Порахував ${data.date ? dayMonth(data.date) : '—'}. Денна норма йде від цієї суми, а витрати до того дня вже в ній. Відкладати цей період застосунок не буде — ці гроші на життя.`
          : `Порахував ${data.date ? dayMonth(data.date) : '—'} — це минулий період, тож зараз бюджет знову з доходу.`}
      </p>

      <FormError>{error}</FormError>

      {data.appliesNow && (
        <button
          onClick={clear}
          disabled={busy}
          className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 py-2.5 font-medium disabled:opacity-40"
        >
          {busy ? 'Прибираю…' : 'Прибрати — рахувати з доходу'}
        </button>
      )}
    </Card>
  )
}

/// Один підрахунок замінює попередній: історія догадок про той самий період тільки
/// породила б питання, яка з них зараз у силі.
function CountForm({ currency, onSet }: {
  currency: string
  onSet: (b: SaveOpeningBalance) => Promise<void>
}) {
  const [amount, setAmount] = useState('')
  const [entryCurrency, setEntryCurrency] = useState(currency)
  const [date, setDate] = useState(todayIso())
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const value = Number(amount.replace(',', '.'))
  const valid = value > 0

  async function save() {
    if (!valid || busy) return
    setBusy(true)
    setError(null)
    try {
      await onSet({ amount: value, currency: entryCurrency, date })
      setAmount('')
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Порахувати заново</SectionTitle>

      <div className="flex gap-2">
        <input
          type="text"
          inputMode="decimal"
          placeholder="0"
          value={amount}
          onChange={(e) => { setAmount(e.target.value); setSaved(false) }}
          aria-label="Сума на руках"
          className="flex-1 text-3xl font-bold tabular-nums bg-transparent outline-none w-full"
        />
        <select
          value={entryCurrency}
          onChange={(e) => setEntryCurrency(e.target.value)}
          aria-label="Валюта"
          className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 font-medium"
        >
          {CURRENCIES.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
      </div>

      {/* День підрахунку, бо з нього рахуються витрати. Вчорашня цифра — теж чесна цифра,
          якщо в банк ти дивився вчора. */}
      <div>
        <label className="text-xs text-neutral-400">Коли дивився</label>
        <input
          type="date"
          value={date}
          max={todayIso()}
          onChange={(e) => setDate(e.target.value)}
          className="mt-1 w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2 text-sm outline-none"
        />
      </div>

      <FormError>{error}</FormError>

      <PrimaryButton onClick={save} disabled={!valid || busy} saved={saved}>
        Це в мене зараз
      </PrimaryButton>
    </Card>
  )
}
