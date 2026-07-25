import { useEffect, useState } from 'react'
import type { SaveTaxProfile, TakeHome, TaxDefaults, TaxProfile } from '../types'
import { money } from '../format'

interface Props {
  profile: TaxProfile | null
  defaults: TaxDefaults | null
  result: TakeHome | null
  onSaveProfile: (p: SaveTaxProfile) => Promise<void>
  onCalculate: (amount: number, includesVat: boolean) => void
  onBack: () => void
}

export function Tax({ profile, defaults, result, onSaveProfile, onCalculate, onBack }: Props) {
  const [showProfile, setShowProfile] = useState(false)
  const [amount, setAmount] = useState('')
  const [includesVat, setIncludesVat] = useState(false)

  const amountNum = Number(amount.replace(',', '.'))
  const canCalc = amountNum > 0

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <button onClick={onBack} className="text-neutral-400 text-2xl leading-none">←</button>
        <h1 className="text-lg font-semibold">Скільки лишиться (B2B)</h1>
      </div>

      {/* Calculator */}
      <div className="rounded-2xl bg-white dark:bg-neutral-900 p-5 shadow-sm space-y-4">
        <input
          inputMode="decimal" placeholder="0" value={amount} autoFocus
          onChange={(e) => setAmount(e.target.value)}
          className="w-full text-4xl font-bold tabular-nums bg-transparent outline-none"
        />

        <div className="flex gap-2">
          {[false, true].map((v) => (
            <button
              key={String(v)} onClick={() => setIncludesVat(v)}
              className={`flex-1 rounded-xl px-3 py-2 text-sm ${
                includesVat === v
                  ? 'bg-neutral-900 dark:bg-white text-white dark:text-neutral-900'
                  : 'bg-neutral-100 dark:bg-neutral-800'
              }`}
            >
              {v ? 'з VAT (brutto)' : 'без VAT (netto)'}
            </button>
          ))}
        </div>

        <button
          onClick={() => onCalculate(amountNum, includesVat)} disabled={!canCalc}
          className="w-full rounded-xl bg-emerald-600 text-white py-3 font-semibold disabled:opacity-40"
        >
          Порахувати
        </button>
      </div>

      {result && <Breakdown r={result} />}

      {/* Profile */}
      <button
        onClick={() => setShowProfile((s) => !s)}
        className="w-full flex items-center justify-between rounded-2xl bg-white dark:bg-neutral-900 px-5 py-4 shadow-sm"
      >
        <span className="text-sm">
          Мій профіль: {profile ? `${profile.regime} ${Math.round(profile.ryczaltRate * 100)}%` : '…'}
          {profile && <span className="text-neutral-400"> · внески {money(profile.monthlyContributionsTotal, 'PLN')}</span>}
        </span>
        <span className="text-neutral-400">{showProfile ? '▾' : '▸'}</span>
      </button>

      {showProfile && profile && (
        <ProfileForm profile={profile} defaults={defaults} onSave={onSaveProfile} />
      )}

      <p className="text-xs text-neutral-400 text-center leading-relaxed">
        Розрахунок орієнтовний, для ryczałt. Ставки на {defaults?.year ?? '—'} рік — звір із
        книговою: вони змінюються щороку.
      </p>
    </div>
  )
}

function Breakdown({ r }: { r: TakeHome }) {
  const row = (label: string, value: number, dim = false) => (
    <div className={`flex justify-between text-sm ${dim ? 'text-neutral-400' : ''}`}>
      <span>{label}</span>
      <span className="tabular-nums">{money(value, r.currency)}</span>
    </div>
  )

  return (
    <div className="rounded-2xl bg-white dark:bg-neutral-900 p-5 shadow-sm space-y-3">
      <div className="text-center">
        <p className="text-sm uppercase tracking-wide text-neutral-400">Реально твоє</p>
        <p className={`mt-1 text-4xl font-bold tabular-nums ${r.takeHome >= 0 ? 'text-emerald-600' : 'text-red-600'}`}>
          {money(r.takeHome, r.currency)}
        </p>
      </div>

      <div className="pt-3 border-t border-neutral-100 dark:border-neutral-800 space-y-1.5">
        {row('Прийшло з VAT', r.grossWithVat, true)}
        {row('VAT (не твій, до US)', -r.vatAmount, true)}
        {row('Przychód (база)', r.revenue)}
        {row('ZUS соцвнески', -r.zusSocial)}
        {row('Складка здоровотна', -r.healthContribution)}
        {row('Податок ryczałt', -r.tax)}
      </div>

      <p className="text-xs text-neutral-400 pt-1">
        База податку {money(r.taxBase, r.currency)} (przychód − ZUS − 50% здоровотної {money(r.healthDeducted, r.currency)})
      </p>
    </div>
  )
}

function ProfileForm({
  profile, defaults, onSave,
}: { profile: TaxProfile; defaults: TaxDefaults | null; onSave: (p: SaveTaxProfile) => Promise<void> }) {
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

      <Field label="ZUS соцвнески, zł/міс">
        <input
          inputMode="decimal" value={form.zusSocial}
          onChange={(e) => set('zusSocial', num(e.target.value))}
          className="w-28 rounded-xl bg-neutral-100 dark:bg-neutral-800 px-3 py-1.5 text-right tabular-nums"
        />
      </Field>

      <Field label="Здоровотна, zł/міс">
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
