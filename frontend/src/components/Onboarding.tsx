import { useState } from 'react'
import { todayIso } from '../types'
import { Card, FormError, PrimaryButton } from './Screen'

/// Перший запуск. До цього перший крок питав «скільки в тебе виходить на місяць» — тобто
/// просив вигадати цифру, яка потім жила в налаштуваннях як «запасний бюджет» і тихо
/// перебивала реальний дохід. Тепер порядок такий, як гроші й приходять: коли зарплата →
/// скільки прийшло → чи є податки → скільки зараз на руках.
interface Props {
  currency: string
  onFinish: (data: {
    periodStartDay: number
    income: number | null
    balance: number | null
    setUpTaxes: boolean
  }) => Promise<void>
  onSkip: () => void
}

export function Onboarding({ currency, onFinish, onSkip }: Props) {
  const [step, setStep] = useState(0)
  // Дата останньої зарплати, з якої додаток сам дістає число дня — 29–31 підтягуються до 28-го,
  // бо їх немає в кожному місяці.
  const [payday, setPayday] = useState(todayIso())
  const typedDay = Number(payday.slice(8, 10))
  const day = Math.min(Number.isNaN(typedDay) ? 1 : typedDay, 28)
  const [income, setIncome] = useState('')
  const [balance, setBalance] = useState('')
  const [setUpTaxes, setSetUpTaxes] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function finish(taxes: boolean) {
    setSaving(true)
    setError(null)
    try {
      await onFinish({
        periodStartDay: day,
        income: parse(income),
        balance: parse(balance),
        setUpTaxes: taxes,
      })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти.')
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <Dots count={4} active={step} />

      {step === 0 && (
        <Step
          title="Коли до тебе приходять гроші?"
          hint={`Зарплата, аванс, оплата від клієнта — головне число місяця. Від нього
                 додаток рахує, на скільки днів треба розтягнути гроші. Якщо приходить
                 кілька разів — став день основної суми.`}
        >
          {/* Дата останнього приходу грошей, а не «число від 1 до 28»: її видно в банку і не
              треба нічого перекладати в голові — той самий пікер, що й у Налаштуваннях. */}
          <input
            type="date"
            value={payday}
            max={todayIso()}
            onChange={(e) => setPayday(e.target.value)}
            aria-label="Дата останньої зарплати"
            className="w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-3 text-lg"
          />
          {/* 29–31 є не в кожному місяці: мовчки взяти 28-ме означало б показати потім період,
              якого людина не обирала. */}
          {typedDay > 28 && (
            <p className="text-xs text-amber-600">
              {typedDay} числа немає в кожному місяці, тому рахуватимемо з 28-го.
            </p>
          )}
          <PrimaryButton onClick={() => setStep(1)}>Далі</PrimaryButton>
        </Step>
      )}

      {step === 1 && (
        <Step
          title="Скільки прийшло цього разу?"
          hint={`Стільки, скільки реально впало на рахунок останнього разу. Це і буде бюджет
                 періоду — вигадувати «скільки виходить на місяць» не треба.`}
        >
          <Amount value={income} onChange={setIncome} currency={currency} placeholder="6000" />
          <PrimaryButton onClick={() => setStep(2)} disabled={parse(income) === null}>
            Далі
          </PrimaryButton>
        </Step>
      )}

      {step === 2 && (
        <Step
          title="Податки з цієї суми платиш сам?"
          hint={`ФОП, B2B, ryczałt — тоді з приходу треба відкласти VAT, ZUS, здоровотну й
                 податок, і витрачати можна значно менше, ніж прийшло. На звичайній
                 зарплаті все це утримали ще до тебе.`}
        >
          <Choice onClick={() => { setSetUpTaxes(false); setStep(3) }}>
            Ні, гроші приходять уже чисті
          </Choice>
          <Choice onClick={() => { setSetUpTaxes(true); setStep(3) }}>
            Так, ФОП / B2B
          </Choice>
        </Step>
      )}

      {step === 3 && (
        <Step
          title="Скільки в тебе є прямо зараз?"
          hint={`Подивись у банку і впиши. Якщо ставиш додаток посеред періоду, частина
                 грошей уже витрачена — і саме ця цифра робить денну норму чесною. Нічого
                 заднім числом вводити не треба.`}
        >
          <Amount value={balance} onChange={setBalance} currency={currency} placeholder="1800" />
          <FormError>{error}</FormError>
          <PrimaryButton onClick={() => void finish(setUpTaxes)} disabled={saving}>
            {saving ? 'Зберігаю…' : setUpTaxes ? 'Далі — податки' : 'Почати'}
          </PrimaryButton>
          <button
            onClick={() => void finish(setUpTaxes)}
            disabled={saving}
            className="w-full text-xs text-neutral-400 py-1"
          >
            Не знаю — пропустити
          </button>
        </Step>
      )}

      <button onClick={onSkip} className="w-full text-xs text-neutral-400 py-2">
        Пропустити, я сам розберусь
      </button>
    </div>
  )
}

function Step({ title, hint, children }: {
  title: string; hint: string; children: React.ReactNode
}) {
  return (
    <Card>
      <h1 className="text-xl font-semibold leading-snug">{title}</h1>
      <p className="text-sm text-neutral-500 leading-relaxed">{hint}</p>
      {children}
    </Card>
  )
}

/// Відповідь-кнопка: тап = відповів і пішов далі. Питання з двома варіантами не варте
/// окремого «Далі».
function Choice({ onClick, children }: { onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 py-3 font-medium"
    >
      {children}
    </button>
  )
}

/// Один великий інпут на крок — на телефоні клавіатура з'їдає пів екрана, і два поля
/// поруч означали б, що друге ніхто не побачить.
function Amount({ value, onChange, currency, placeholder }: {
  value: string; onChange: (v: string) => void; currency: string; placeholder: string
}) {
  return (
    <div className="flex items-baseline gap-2">
      <input
        type="number"
        inputMode="decimal"
        autoFocus
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="w-full bg-transparent text-4xl font-bold tabular-nums outline-none"
      />
      <span className="text-lg text-neutral-400 shrink-0">{currency}</span>
    </div>
  )
}

function Dots({ count, active }: { count: number; active: number }) {
  return (
    <div className="flex justify-center gap-1.5 pt-2">
      {Array.from({ length: count }, (_, i) => (
        <span
          key={i}
          className={`h-1.5 rounded-full transition-all ${
            i === active ? 'w-6 bg-neutral-900 dark:bg-white' : 'w-1.5 bg-neutral-300 dark:bg-neutral-700'
          }`}
        />
      ))}
    </div>
  )
}

/// Порожнє поле — це «пропустив», а не нуль: нуль означав би дохід у нуль злотих.
function parse(v: string): number | null {
  const n = Number(v.replace(',', '.'))
  return v.trim() === '' || Number.isNaN(n) || n <= 0 ? null : n
}
