import { useEffect, useState } from 'react'
import type { SaveTaxProfile, TaxDefaults, TaxProfile as TaxProfileData } from '../types'
import { money } from '../format'

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
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onBack} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">Податковий профіль</h1>
      </div>

      <p className="text-sm text-neutral-500">
        За цими ставками рахується твій бюджет місяця, коли ти вписуєш дохід.
      </p>

      {profile
        ? <ProfileForm profile={profile} defaults={defaults} onSave={onSave} />
        : <div className="rounded-2xl bg-white dark:bg-neutral-900 p-6 shadow-sm animate-pulse h-40" />}

      <p className="text-xs text-neutral-400 text-center leading-relaxed">
        Розрахунок орієнтовний, для ryczałt. Ставки на {defaults?.year ?? '—'} рік — звір із
        книговою: вони змінюються щороку.
      </p>
    </div>
  )
}

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
  })
  const [saved, setSaved] = useState(false)

  useEffect(() => setSaved(false), [form])

  const set = <K extends keyof SaveTaxProfile>(k: K, v: SaveTaxProfile[K]) =>
    setForm((f) => ({ ...f, [k]: v }))

  const num = (v: string) => Number(v.replace(',', '.')) || 0

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-5 shadow-sm space-y-4">
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

      <button
        onClick={() => onSave(form).then(() => setSaved(true))}
        className="w-full rounded-xl bg-neutral-900 dark:bg-white text-white dark:text-neutral-900 py-2.5 font-medium"
      >
        {saved ? 'Збережено ✓' : 'Зберегти профіль'}
      </button>
    </div>
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
