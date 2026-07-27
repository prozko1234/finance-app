import { useEffect, useState } from 'react'
import type { SaveTaxProfile, TaxDefaults, TaxRegime, TaxProfile as TaxProfileData } from '../types'
import { money } from '../format'
import { Card, CardSkeleton, FormError, PrimaryButton, Screen } from './Screen'

interface Props {
  profile: TaxProfileData | null
  defaults: TaxDefaults | null
  onSave: (p: SaveTaxProfile) => Promise<void>
  onBack: () => void
}

/// M17: the standalone calculator is gone — the income form already answers "скільки
/// лишиться", and two places computing the same number is how they start to disagree.
/// What stays here is the profile: the rates that calculation runs on.
export function TaxProfile({ profile, defaults, onSave, onBack }: Props) {
  return (
    <Screen
      title="Податковий профіль"
      onBack={onBack}
      subtitle="Звідси береться твій бюджет місяця, коли ти вписуєш дохід."
      footnote={profile && profile.regime !== 'None'
        ? `Розрахунок орієнтовний. Ставки на ${defaults?.year ?? '—'} рік — звір із книговою: вони змінюються щороку.`
        : undefined}
    >
      {profile
        ? <ProfileForm profile={profile} defaults={defaults} onSave={onSave} />
        : <CardSkeleton />}
    </Screen>
  )
}

const REGIMES: { value: TaxRegime; label: string; hint: string }[] = [
  { value: 'None', label: 'Просто гроші', hint: 'Скільки прийшло — стільки й твоє' },
  { value: 'Ryczalt', label: 'B2B, ryczałt', hint: 'VAT, ZUS і здоровотна відкладаються самі' },
  { value: 'UoP', label: 'Умова о праце', hint: 'Brutto з умови → netto на руки' },
  { value: 'Zlecenie', label: 'Умова злеценя', hint: 'Brutto з умови → netto на руки' },
]

function ProfileForm({
  profile, defaults, onSave,
}: { profile: TaxProfileData; defaults: TaxDefaults | null; onSave: (p: SaveTaxProfile) => Promise<void> }) {
  const [form, setForm] = useState<SaveTaxProfile>({
    regime: profile.regime,
    ryczaltRate: profile.ryczaltRate,
    vatPayer: profile.vatPayer,
    vatRate: profile.vatRate,
    zusType: profile.zusType,
    zusSocial: profile.zusSocial,
    healthContribution: profile.healthContribution,
    chorobowe: profile.chorobowe,
    studentUnder26: profile.studentUnder26,
  })
  const [saved, setSaved] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => setSaved(false), [form])

  async function save() {
    setBusy(true)
    setError(null)
    try {
      await onSave(form)
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося зберегти')
    } finally {
      setBusy(false)
    }
  }

  const set = <K extends keyof SaveTaxProfile>(k: K, v: SaveTaxProfile[K]) =>
    setForm((f) => ({ ...f, [k]: v }))

  return (
    <Card>
      <div className="space-y-2">
        {REGIMES.map((r) => (
          <button
            key={r.value}
            onClick={() => set('regime', r.value)}
            className={`w-full rounded-xl px-3 py-2.5 text-left ${
              form.regime === r.value
                ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                : 'bg-neutral-100 dark:bg-neutral-800'
            }`}
          >
            <span className="text-sm font-medium">{r.label}</span>
            <span className={`block text-xs ${form.regime === r.value ? 'opacity-70' : 'text-neutral-400'}`}>
              {r.hint}
            </span>
          </button>
        ))}
      </div>

      {/* Only the fields the chosen regime actually uses — the rest would be noise. */}
      {form.regime === 'None' && (
        <p className="text-sm text-neutral-500">
          Скільки вписав — стільки й твоє. Ні податків, ні внесків рахувати не треба.
        </p>
      )}

      {form.regime === 'Ryczalt' && <RyczaltFields form={form} set={set} defaults={defaults} />}

      {form.regime === 'UoP' && (
        <p className="text-sm text-neutral-500">
          Налаштовувати нічого: ZUS, здоровотна і PIT на умові о праце однакові для всіх.
          Вписуй brutto з умови — покажу, скільки прийде на рахунок.
        </p>
      )}

      {form.regime === 'Zlecenie' && (
        <div className="space-y-4">
          <Field label="Студент до 26 років">
            <input
              type="checkbox" checked={form.studentUnder26}
              onChange={(e) => set('studentUnder26', e.target.checked)} className="h-5 w-5"
            />
          </Field>
          {form.studentUnder26
            ? <p className="text-sm text-neutral-500">Ні ZUS, ні податку — brutto і є твої гроші.</p>
            : (
              <Field label="Добровільне chorobowe">
                <input
                  type="checkbox" checked={form.chorobowe}
                  onChange={(e) => set('chorobowe', e.target.checked)} className="h-5 w-5"
                />
              </Field>
            )}
        </div>
      )}

      <FormError>{error}</FormError>

      <PrimaryButton onClick={save} disabled={busy} saved={saved}>
        Зберегти профіль
      </PrimaryButton>
    </Card>
  )
}

/// Ryczalt is the only regime with negotiable numbers: the rate, VAT status and the ZUS
/// amounts the accountant bills. Everything else is statutory and needs no form.
function RyczaltFields({ form, set, defaults }: {
  form: SaveTaxProfile
  set: <K extends keyof SaveTaxProfile>(k: K, v: SaveTaxProfile[K]) => void
  defaults: TaxDefaults | null
}) {
  const num = (v: string) => Number(v.replace(',', '.')) || 0

  return (
    <>
      <Field label="Ставка ryczałt, %">
        <input
          inputMode="decimal" value={Math.round(form.ryczaltRate * 1000) / 10}
          onChange={(e) => set('ryczaltRate', num(e.target.value) / 100)}
          className="w-20 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5 text-right tabular-nums"
        />
      </Field>

      <Field label="Платник VAT">
        <input type="checkbox" checked={form.vatPayer} onChange={(e) => set('vatPayer', e.target.checked)} className="h-5 w-5" />
      </Field>

      {form.vatPayer && (
        <Field label="Ставка VAT, %">
          <input
            inputMode="decimal" value={Math.round(form.vatRate * 1000) / 10}
            onChange={(e) => set('vatRate', num(e.target.value) / 100)}
            className="w-20 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5 text-right tabular-nums"
          />
        </Field>
      )}

      <Field label="ZUS, соціальні внески (zł/міс)">
        <input
          inputMode="decimal" value={form.zusSocial}
          onChange={(e) => set('zusSocial', num(e.target.value))}
          className="w-28 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5 text-right tabular-nums"
        />
      </Field>

      <Field label="Здоровотна (zł/міс)">
        <input
          inputMode="decimal" value={form.healthContribution}
          onChange={(e) => set('healthContribution', num(e.target.value))}
          className="w-28 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5 text-right tabular-nums"
        />
      </Field>

      <div className="flex justify-between text-sm pt-1">
        <span className="text-neutral-400">Разом на місяць</span>
        <span className="font-semibold tabular-nums">{money(form.zusSocial + form.healthContribution, 'PLN')}</span>
      </div>

      {defaults && (
        <div className="text-xs text-neutral-400 space-y-1 pt-2 border-t border-neutral-100 dark:border-neutral-800">
          <p>Підказки {defaults.year}: duży без chorobowego {money(defaults.duzyWithoutChorobowe, 'PLN')}, з ним {money(defaults.duzyWithChorobowe, 'PLN')}.</p>
          <p>Здоровотна: до 60k {money(defaults.healthUnder60k, 'PLN')} · 60–300k {money(defaults.health60kTo300k, 'PLN')} · 300k+ {money(defaults.healthOver300k, 'PLN')}.</p>
        </div>
      )}
    </>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span className="text-sm text-neutral-500">{label}</span>
      {children}
    </div>
  )
}
