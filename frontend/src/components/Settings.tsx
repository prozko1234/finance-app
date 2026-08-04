import { useState } from 'react'
import type { AppSettings } from '../types'
import { CURRENCIES, todayIso } from '../types'
import { dayMonth } from '../format'
import { Card, FormError, Screen, SectionTitle } from './Screen'

interface Props {
  settings: AppSettings | null
  onPickCurrency: (currency: string) => Promise<void>
  onPickPeriodStartDay: (day: number) => Promise<void>
  onBack: () => void
}

/// Settings are settings only. The screens that used to hang off this page — categories,
/// subscriptions, the tax profile — live in the menu now, where they read as places rather
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

/// The day the money arrives. Until this, the app assumed a month starts on the 1st — so in
/// the last days of the month it promised a norm out of money the account no longer had, and
/// on the 1st the figure jumped although no salary had landed yet.
///
/// It asks for the DATE money last arrived, not for "a number from 1 to 28": that date is
/// visible in the banking app and needs no translating in one's head. The app works the day
/// out itself and immediately shows which period it produces.
function PaydayCard({ settings, onPick }: {
  settings: AppSettings | null
  onPick: (day: number) => Promise<void>
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // What was picked last, only to explain a discrepancy — not as the picker's state.
  const [typed, setTyped] = useState<string | null>(null)

  const day = settings?.periodStartDay ?? 1
  // The picker shows the date the app actually remembered, not what was typed. This used to
  // be its own state, filled in once on the first render: while the settings were still in
  // flight the picker kept "today" forever, and the period beneath it said something else —
  // two dates on one screen that did not agree.
  const shown = settings?.periodStart ?? todayIso()
  const value = busy ? typed ?? shown : shown

  const typedDay = typed === null ? null : Number(typed.slice(8, 10))
  // 29–31 do not exist in every month: "the 31st" would quietly mean four different dates a
  // year. Rather than forbidding the date or saying nothing, it takes the 28th and says so.
  const clamped = typedDay !== null && typedDay > 28
  // The same date in a different month: only the day is stored, so the picker jumps to the
  // most recent payday on that day. That is worth saying, not silently doing.
  const moved = typed !== null && !clamped && typed !== shown && !busy

  async function pick(iso: string) {
    setTyped(iso)
    const next = Math.min(Number(iso.slice(8, 10)), 28)
    if (busy || Number.isNaN(next) || next === day) return

    setBusy(true)
    setError(null)
    try {
      await onPick(next)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося змінити день')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Коли востаннє прийшли гроші</SectionTitle>

      <input
        type="date"
        value={value}
        disabled={busy}
        onChange={(e) => void pick(e.target.value)}
        aria-label="Дата останньої зарплати"
        className="w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2.5 disabled:opacity-40"
      />

      <FormError>{error}</FormError>

      {/* The point: what is shown is not "day 10" but what that day produces. */}
      {settings && (
        <div className="rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-2.5 space-y-1">
          <p className="text-sm font-medium">
            {busy
              ? 'Рахую…'
              : `Період: ${dayMonth(settings.periodStart)} – ${dayMonth(settings.periodEnd)}`}
          </p>
          <p className="text-xs text-neutral-500">
            {day === 1
              ? 'Гроші приходять 1 числа — період збігається з календарним місяцем.'
              : `Далі гроші чекаємо ${day} числа кожного місяця. Бюджет, денна норма й банки рахуються від зарплати до зарплати.`}
          </p>
          {clamped && (
            <p className="text-xs text-amber-600">
              {typedDay} числа немає в кожному місяці, тому рахуємо з 28-го.
            </p>
          )}
          {moved && (
            <p className="text-xs text-neutral-500">
              Запамʼятали тільки число ({day}) — остання зарплата за ним була {dayMonth(shown)}.
            </p>
          )}
        </div>
      )}
    </Card>
  )
}

/// The reading currency. One tap applies it, like the allocation schemes: a "pick, then save"
/// step is one more decision for nothing.
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
