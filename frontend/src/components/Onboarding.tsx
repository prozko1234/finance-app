import { useState } from 'react'
import { Card, FormError, PrimaryButton } from './Screen'

/// Три кроки при першому запуску. До них головна показувала «Бюджет ще не заданий» —
/// технічно правда, але новий користувач не знає ні що таке бюджет тут, ні звідки він
/// береться, ні чому цифра завищена, якщо ставити додаток 20-го числа.
///
/// Другий крок — головний: залишок грошей. Саме він робить чесною денну норму для того,
/// хто почав користуватись усередині місяця.
interface Props {
  currency: string
  onFinish: (data: { budget: number | null; balance: number | null }) => Promise<void>
  onSkip: () => void
}

export function Onboarding({ currency, onFinish, onSkip }: Props) {
  const [step, setStep] = useState(0)
  const [budget, setBudget] = useState('')
  const [balance, setBalance] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const today = new Date().getDate()

  async function finish() {
    setSaving(true)
    setError(null)
    try {
      await onFinish({ budget: parse(budget), balance: parse(balance) })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти.')
      setSaving(false)
    }
  }

  return (
    <div className="space-y-5">
      <Dots count={3} active={step} />

      {step === 0 && (
        <Step
          title="Скільки в тебе виходить на місяць?"
          hint={`Приблизно — це просто відправна точка. Якщо потім заведеш дохід, додаток
                 порахує суму після податків сам і замінить цю цифру.`}
        >
          <Amount value={budget} onChange={setBudget} currency={currency} placeholder="6000" />
          <PrimaryButton onClick={() => setStep(1)} disabled={parse(budget) === null}>
            Далі
          </PrimaryButton>
        </Step>
      )}

      {step === 1 && (
        <Step
          title="А скільки в тебе є прямо зараз?"
          hint={`Стільки, скільки маєш витратити до кінця місяця — подивись у банку і впиши.
                 Нічого не треба вводити заднім числом: усе, що витрачено до сьогодні, вже
                 всередині цієї цифри.`}
        >
          <Amount value={balance} onChange={setBalance} currency={currency} placeholder="1800" />
          <PrimaryButton onClick={() => setStep(2)} disabled={parse(balance) === null}>
            Далі
          </PrimaryButton>
          {today === 1 && (
            <button onClick={() => setStep(2)} className="w-full text-xs text-neutral-400 py-1">
              Сьогодні 1 число — можна пропустити
            </button>
          )}
        </Step>
      )}

      {step === 2 && (
        <Step
          title="Це все"
          hint={`Далі — одна цифра на головній: скільки можна витратити сьогодні. Кнопка «+»
                 внизу додає витрату, і цифра одразу перераховується. Вести облік не треба.`}
        >
          <FormError>{error}</FormError>
          <PrimaryButton onClick={finish} disabled={saving}>
            {saving ? 'Зберігаю…' : 'Почати'}
          </PrimaryButton>
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

/// Порожнє поле — це «пропустив», а не нуль: нуль означав би бюджет у нуль злотих.
function parse(v: string): number | null {
  const n = Number(v.replace(',', '.'))
  return v.trim() === '' || Number.isNaN(n) || n < 0 ? null : n
}
