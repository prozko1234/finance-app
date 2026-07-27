import { useState } from 'react'
import type { Budget } from '../types'
import { money } from '../format'
import { Card, FormError, PrimaryButton, Screen } from './Screen'

interface Props {
  budget: Budget | null
  /// This month's budget derived from income, when there is income. While it is set,
  /// the manual amount below is ignored — M17 stopped the UI from pretending otherwise.
  incomeBudget: number | null
  onSave: (amount: number) => Promise<void>
  onBack: () => void
}

/// Settings are settings only. The screens that used to hang off this page — категорії,
/// підписки, податковий профіль — live in the menu now, where they read as places rather
/// than as options of something else.
export function Settings({ budget, incomeBudget, onSave, onBack }: Props) {
  const [amount, setAmount] = useState(budget?.monthlyAmount != null ? String(budget.monthlyAmount) : '')
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const amountNum = Number(amount.replace(',', '.'))
  const valid = amountNum >= 0 && amount.trim() !== ''

  async function submit() {
    if (!valid) return
    setSaving(true)
    setError(null)
    setSaved(false)
    try {
      await onSave(amountNum)
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setSaving(false)
    }
  }

  return (
    <Screen
      title="Бюджет і решта"
      onBack={onBack}
      subtitle="Запасний бюджет — коли за місяць немає доходу."
      footnote="«Ще сьогодні» рахується від бюджета місяця: з доходу, якщо він є, інакше — із запасного. Банківський синк — у майбутніх версіях."
    >
      <Card>
        <p className="text-xs text-neutral-400">
          {incomeBudget !== null
            ? `Цього місяця не діє: бюджет уже порахований з доходу — ${money(incomeBudget, budget?.currency ?? 'PLN')}. Ця сума спрацює в місяці без доходу.`
            : 'Діє, поки за місяць немає доходу. Щойно впишеш дохід, бюджет порахується з нього.'}
        </p>
        <div className="flex gap-2 items-baseline">
          <input
            type="text"
            inputMode="decimal"
            placeholder="0"
            value={amount}
            onChange={(e) => { setAmount(e.target.value); setSaved(false) }}
            className="flex-1 text-3xl font-bold tabular-nums bg-transparent outline-none"
          />
          <span className="text-neutral-400 font-medium">zł</span>
        </div>
        <FormError>{error}</FormError>
        <PrimaryButton onClick={submit} disabled={!valid || saving} saved={saved}>
          {saving ? 'Зберігаю…' : 'Зберегти бюджет'}
        </PrimaryButton>
      </Card>
    </Screen>
  )
}
