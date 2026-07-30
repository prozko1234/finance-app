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
///
/// Питаємо не «число від 1 до 28», а дату останнього приходу грошей: її видно в банку і не
/// треба нічого перекладати в голові. Число дня додаток дістає сам і одразу показує, який
/// період із цього виходить.
function PaydayCard({ settings, onPick }: {
  settings: AppSettings | null
  onPick: (day: number) => Promise<void>
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Що людина обрала останнім — тільки щоб пояснити розбіжність, не як стан пікера.
  const [typed, setTyped] = useState<string | null>(null)

  const day = settings?.periodStartDay ?? 1
  // Пікер показує ту дату, яку додаток справді запамʼятав, а не те, що набрали. Раніше це
  // був окремий стан, заповнений один раз при першому рендері: коли налаштування ще не
  // приїхали, у пікері лишалось «сьогодні» назавжди, а період під ним показував інше — два
  // числа на одному екрані, які не сходяться.
  const shown = settings?.periodStart ?? todayIso()
  const value = busy ? typed ?? shown : shown

  const typedDay = typed === null ? null : Number(typed.slice(8, 10))
  // 29–31 є не в кожному місяці: «31 число» тихо означало б чотири різні дати на рік.
  // Не мовчимо про це і не забороняємо дату — беремо 28-ме і кажемо, що взяли.
  const clamped = typedDay !== null && typedDay > 28
  // Обрали ту саму дату іншого місяця: зберігається лише число, тож пікер стрибне на
  // останню зарплату за цим числом. Це теж треба сказати, а не молча переставити.
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

      {/* Головне: людина бачить не «число 10», а що з цього виходить. */}
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
