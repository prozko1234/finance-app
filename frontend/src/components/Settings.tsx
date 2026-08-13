import { useState } from 'react'
import type { AppSettings, PushStatus } from '../types'
import { CURRENCIES, todayIso } from '../types'
import { dayMonth } from '../format'
import { disablePush, enablePush, pushSupported, type PushProblem } from '../push'
import { Card, FormError, Screen, SectionTitle } from './Screen'

interface Props {
  settings: AppSettings | null
  push: PushStatus | null
  onPickCurrency: (currency: string) => Promise<void>
  onPickPeriodStartDay: (day: number) => Promise<void>
  onPickReminderHour: (hour: number | null) => Promise<void>
  /// Re-reads the push status after the browser side of subscribing has finished.
  onPushChanged: () => void
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
export function Settings({
  settings, push, onPickCurrency, onPickPeriodStartDay, onPickReminderHour, onPushChanged, onBack,
}: Props) {
  return (
    <Screen
      title="Налаштування"
      onBack={onBack}
      subtitle="Коли приходять гроші, в якій валюті все читати і коли нагадувати."
      footnote="Бюджет береться з доходів за період. Банківський синк — у майбутніх версіях."
    >
      <PaydayCard settings={settings} onPick={onPickPeriodStartDay} />
      <RemindersCard status={push} onPickHour={onPickReminderHour} onChanged={onPushChanged} />
      <CurrencyCard settings={settings} onPick={onPickCurrency} />
    </Screen>
  )
}

/// «Нагадай, коли сьогодні щось списується».
///
/// The hour is the whole feature. A charge falls due at midnight, and midnight is exactly the
/// wrong moment to say so: the notification is read the next morning with the rest of the
/// night's noise, by which time the money has gone and there is nothing left to decide.
///
/// Every way this can fail is a way the user cannot see, so each one gets a sentence: an
/// iPhone will not subscribe a page that lives in a tab, a refused permission is refused in the
/// browser and not here, and a server with no keys simply cannot send anything.
const PROBLEMS: Record<PushProblem, string> = {
  unsupported: 'Цей браузер не вміє пуш-сповіщень.',
  'needs-install':
    'На айфоні спершу додай застосунок на екран «Додому» — інакше Safari не дає підписатись.',
  denied: 'Сповіщення заборонені в налаштуваннях браузера. Дозволь їх там і спробуй ще раз.',
  'no-server-key': 'На сервері не налаштовані ключі для сповіщень — поки що нема чим слати.',
  failed: 'Не вийшло підписатись. Спробуй ще раз.',
}

/// The hours worth offering. A full 0–23 picker is a decision with twenty-four answers; these
/// four are the times a phone is actually in a hand.
const HOURS = [8, 10, 14, 19]

function RemindersCard({ status, onPickHour, onChanged }: {
  status: PushStatus | null
  onPickHour: (hour: number | null) => Promise<void>
  onChanged: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const supported = pushSupported()
  const on = status?.enabled === true && status.hour !== null

  async function toggle() {
    setBusy(true)
    setError(null)
    try {
      if (on) {
        await disablePush()
        await onPickHour(null)
      } else {
        const problem = await enablePush()
        if (problem) setError(PROBLEMS[problem])
      }
      onChanged()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вийшло змінити сповіщення.')
    } finally {
      setBusy(false)
    }
  }

  async function pick(hour: number) {
    if (busy || hour === status?.hour) return
    setBusy(true)
    setError(null)
    try {
      await onPickHour(hour)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вийшло змінити годину.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Нагадувати про списання</SectionTitle>

      <p className="text-sm text-neutral-500">
        Раз на день, якщо сьогодні щось має списатись. Одне сповіщення, не по одному на платіж.
      </p>

      <button
        onClick={toggle}
        disabled={busy || !supported}
        className={`w-full rounded-xl px-4 py-2.5 text-sm font-medium disabled:opacity-40 ${
          on
            ? 'bg-neutral-100 dark:bg-neutral-800 text-neutral-500'
            : 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
        }`}
      >
        {busy ? '…' : on ? 'Вимкнути' : 'Увімкнути сповіщення'}
      </button>

      {on && (
        <div className="space-y-2">
          <p className="text-xs text-neutral-400">О котрій</p>
          <div className="flex gap-2">
            {HOURS.map((h) => (
              <button
                key={h}
                onClick={() => pick(h)}
                disabled={busy}
                aria-pressed={status?.hour === h}
                className={`flex-1 rounded-xl px-3 py-2 text-sm font-medium disabled:opacity-40 ${
                  status?.hour === h
                    ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                    : 'bg-neutral-100 dark:bg-neutral-800'
                }`}
              >
                {String(h).padStart(2, '0')}:00
              </button>
            ))}
          </div>
        </div>
      )}

      <FormError>{error}</FormError>

      {!supported && (
        <p className="text-xs text-neutral-400">{PROBLEMS.unsupported}</p>
      )}
    </Card>
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
