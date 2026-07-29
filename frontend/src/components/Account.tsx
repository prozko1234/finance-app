import { useState } from 'react'
import { Card, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

/// Мінімальна довжина пароля — та сама, що на сервері (AccountService.MinPasswordLength).
/// Тут вона лише щоб не відправляти запит, який точно повернеться помилкою.
const MIN_PASSWORD = 10

interface Props {
  email: string | null
  onChangePassword: (current: string, next: string) => Promise<void>
  onChangeEmail: (password: string, email: string) => Promise<void>
  onSignOutEverywhere: () => Promise<void>
  onLogout: () => void
  onBack: () => void
}

/// Все про сам акаунт в одному місці. До цього пароль жив у змінній оточення: щоб його
/// змінити, треба було лізти в Coolify і передеплоїти, а сесію на чужому пристрої не
/// можна було закрити взагалі.
export function Account({
  email, onChangePassword, onChangeEmail, onSignOutEverywhere, onLogout, onBack,
}: Props) {
  return (
    <Screen
      title="Акаунт"
      onBack={onBack}
      subtitle={email ? `Ти увійшов як ${email}.` : undefined}
      footnote="Пароль зберігається тільки як хеш — його не знає ні додаток, ні я. Якщо забудеш, відновити нема з чого."
    >
      <PasswordCard onSave={onChangePassword} />
      <EmailCard email={email} onSave={onChangeEmail} />
      <SessionsCard onSignOutEverywhere={onSignOutEverywhere} onLogout={onLogout} />
    </Screen>
  )
}

function PasswordCard({ onSave }: { onSave: (current: string, next: string) => Promise<void> }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const ready = current !== '' && next.length >= MIN_PASSWORD

  async function submit() {
    if (!ready || busy) return
    setBusy(true)
    setError(null)
    setSaved(false)
    try {
      await onSave(current, next)
      setSaved(true)
      setCurrent('')
      setNext('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося змінити пароль')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Пароль</SectionTitle>
      <Field label="Поточний" type="password" value={current} autoComplete="current-password"
        onChange={(v) => { setCurrent(v); setSaved(false) }} />
      <Field label="Новий" type="password" value={next} autoComplete="new-password"
        onChange={(v) => { setNext(v); setSaved(false) }} />
      <p className="text-xs text-neutral-400">
        Не коротший за {MIN_PASSWORD} символів. Після зміни решта пристроїв вилетить —
        цей залишиться.
      </p>
      <FormError>{error}</FormError>
      <PrimaryButton onClick={() => void submit()} disabled={!ready || busy} saved={saved}>
        {busy ? 'Міняю…' : 'Змінити пароль'}
      </PrimaryButton>
    </Card>
  )
}

function EmailCard({ email, onSave }: {
  email: string | null
  onSave: (password: string, email: string) => Promise<void>
}) {
  const [value, setValue] = useState(email ?? '')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const changed = value.trim().toLowerCase() !== (email ?? '').toLowerCase()
  const ready = changed && value.includes('@') && password !== ''

  async function submit() {
    if (!ready || busy) return
    setBusy(true)
    setError(null)
    setSaved(false)
    try {
      await onSave(password, value.trim())
      setSaved(true)
      setPassword('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося змінити пошту')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Пошта</SectionTitle>
      <Field label="Пошта" type="email" value={value} autoComplete="username"
        onChange={(v) => { setValue(v); setSaved(false) }} />
      <Field label="Пароль, щоб підтвердити" type="password" value={password}
        autoComplete="current-password" onChange={setPassword} />
      <p className="text-xs text-neutral-400">
        Це просто ім'я входу — листів сюди ніхто не шле.
      </p>
      <FormError>{error}</FormError>
      <PrimaryButton onClick={() => void submit()} disabled={!ready || busy} saved={saved}>
        {busy ? 'Міняю…' : 'Змінити пошту'}
      </PrimaryButton>
    </Card>
  )
}

function SessionsCard({ onSignOutEverywhere, onLogout }: {
  onSignOutEverywhere: () => Promise<void>
  onLogout: () => void
}) {
  const [busy, setBusy] = useState(false)

  return (
    <Card>
      <SectionTitle>Сесії</SectionTitle>
      <p className="text-xs text-neutral-400">
        Вхід тримається місяць і продовжується сам, поки ти заходиш. Якщо телефон загубився —
        закрий усі сесії: разом із цією.
      </p>
      <button
        onClick={onLogout}
        className="w-full rounded-xl bg-neutral-100 dark:bg-neutral-800 py-2.5 font-medium"
      >
        Вийти на цьому пристрої
      </button>
      <button
        onClick={() => { setBusy(true); void onSignOutEverywhere().finally(() => setBusy(false)) }}
        disabled={busy}
        className="w-full rounded-xl py-2.5 font-medium text-red-600 disabled:opacity-40"
      >
        {busy ? 'Закриваю…' : 'Вийти з усіх пристроїв'}
      </button>
    </Card>
  )
}

function Field({ label, type, value, autoComplete, onChange }: {
  label: string
  type: 'password' | 'email'
  value: string
  autoComplete: string
  onChange: (v: string) => void
}) {
  return (
    <label className="block space-y-2">
      <span className="text-sm text-neutral-500">{label}</span>
      <input
        type={type}
        value={value}
        autoComplete={autoComplete}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2.5"
      />
    </label>
  )
}
