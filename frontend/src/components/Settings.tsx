import { useState } from 'react'
import type { AppSettings } from '../types'
import { CURRENCIES } from '../types'
import { dayMonth } from '../format'
import { Card, FormError, Screen, SectionTitle } from './Screen'

interface Props {
  settings: AppSettings | null
  onPickCurrency: (currency: string) => Promise<void>
  onPickPeriodStartDay: (day: number) => Promise<void>
  onBack: () => void
}

/// Settings are settings only. The screens that used to hang off this page — категорії,
/// підписки, податковий профіль — live in the menu now, where they read as places rather
/// than as options of something else.
///
/// The "запасний бюджет" card used to live here: an amount typed once that quietly took
/// over whenever income was missing. It was a second answer to "скільки в мене грошей",
/// free to disagree with the first one for months. The budget now comes from money that
/// actually arrived, and nothing here can override it.
export function Settings({ settings, onPickCurrency, onPickPeriodStartDay, onBack }: Props) {
  return (
    <Screen
      title="Налаштування"
      onBack={onBack}
      subtitle="Коли приходять гроші і в якій валюті все читати."
      footnote="Бюджет береться з доходів за період. Банківський синк — у майбутніх версіях."
    >
      <PaydayCard settings={settings} onPick={onPickPeriodStartDay} />
      <CurrencyCard settings={settings} onPick={onPickCurrency} />
    </Screen>
  )
}

/// День, коли приходять гроші. Доти додаток вважав, що місяць починається 1 числа — і в
/// останні дні місяця обіцяв норму з грошей, яких на рахунку вже не було, а 1-го вона
/// стрибала, хоча зарплата ще не прийшла.
function PaydayCard({ settings, onPick }: {
  settings: AppSettings | null
  onPick: (day: number) => Promise<void>
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const day = settings?.periodStartDay ?? 1

  async function pick(value: number) {
    if (busy || value === day) return
    setBusy(true)
    setError(null)
    try {
      await onPick(value)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося змінити день')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Коли приходять гроші</SectionTitle>

      <div className="flex gap-2 items-baseline">
        <select
          value={day}
          disabled={busy}
          onChange={(e) => void pick(Number(e.target.value))}
          aria-label="День зарплати"
          className="flex-1 rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2.5 disabled:opacity-40"
        >
          {/* Тільки до 28: 29–31 є не в кожному місяці, і «30 число» тихо означало б
              чотири різні дати на рік. */}
          {Array.from({ length: 28 }, (_, i) => i + 1).map((d) => (
            <option key={d} value={d}>{d} числа</option>
          ))}
        </select>
      </div>

      <FormError>{error}</FormError>

      <p className="text-xs text-neutral-400">
        {settings && day !== 1
          ? `Поточний період: ${dayMonth(settings.periodStart)} – ${dayMonth(settings.periodEnd)}. Бюджет, денна норма й конверти рахуються від зарплати до зарплати.`
          : 'Зараз місяць рахується з 1 числа. Якщо зп приходить в інший день — постав його, і все почне рахуватись від зарплати до зарплати.'}
      </p>
    </Card>
  )
}

/// Валюта читання. Один тап = застосовано, як і схеми розподілу: зайвий крок
/// «обери, потім збережи» — це зайве рішення.
function CurrencyCard({ settings, onPick }: {
  settings: AppSettings | null
  onPick: (currency: string) => Promise<void>
}) {
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function pick(c: string) {
    if (busy || c === settings?.displayCurrency) return
    setBusy(c)
    setError(null)
    try {
      await onPick(c)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося змінити валюту')
    } finally {
      setBusy(null)
    }
  }

  return (
    <Card>
      <SectionTitle>Валюта</SectionTitle>

      <div className="flex gap-2">
        {CURRENCIES.map((c) => (
          <button
            key={c}
            onClick={() => pick(c)}
            disabled={busy !== null}
            aria-pressed={settings?.displayCurrency === c}
            className={`flex-1 rounded-xl px-3 py-2 text-sm font-medium disabled:opacity-40 ${
              settings?.displayCurrency === c
                ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                : 'bg-neutral-100 dark:bg-neutral-800'
            }`}
          >
            {busy === c ? '…' : c}
          </button>
        ))}
      </div>

      <FormError>{error}</FormError>

      <p className="text-xs text-neutral-400">
        {settings && settings.displayCurrency !== settings.baseCurrency
          ? `Записи зберігаються у ${settings.baseCurrency} і не переписуються — валюта міняє тільки те, як ти їх читаєш. Кожна транзакція перераховується за курсом своєї дати, тож минуле не змінює розмір.`
          : 'Все показується у злотих. Можна читати додаток в іншій валюті — записи від цього не змінюються.'}
      </p>
    </Card>
  )
}
