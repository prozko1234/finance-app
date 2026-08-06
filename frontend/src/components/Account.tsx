import { useState } from 'react'
import type { Device } from '../api'
import type { Invite, NewInvite } from '../types'
import { dayMonth } from '../format'
import { Card, FormError, PrimaryButton, Screen, SectionTitle } from './Screen'

/// The minimum password length, the same as the server's (AccountService.MinPasswordLength).
/// It is here only to avoid sending a request that is certain to come back an error.
const MIN_PASSWORD = 10

interface Props {
  email: string | null
  onChangePassword: (current: string, next: string) => Promise<void>
  onChangeEmail: (password: string, email: string) => Promise<void>
  onSignOutEverywhere: () => Promise<void>
  onLogout: () => void
  /// Devices that sign in with a token. Empty until there is a native app.
  devices: Device[]
  onRevokeDevice: (id: number) => Promise<void>
  /// Only the owner of the instance may invite, and only then is any of this passed in.
  isOwner: boolean
  invites: Invite[]
  onCreateInvite: (note: string) => Promise<NewInvite>
  onRevokeInvite: (id: number) => Promise<void>
  onBack: () => void
}

/// Everything about the account itself in one place. The password used to live in an
/// environment variable: changing it meant going into Coolify and redeploying, and a session on
/// somebody else's device could not be closed at all.
export function Account({
  email, onChangePassword, onChangeEmail, onSignOutEverywhere, onLogout,
  devices, onRevokeDevice, isOwner, invites, onCreateInvite, onRevokeInvite, onBack,
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
      {devices.length > 0 && <DevicesCard devices={devices} onRevoke={onRevokeDevice} />}
      {isOwner && (
        <InvitesCard invites={invites} onCreate={onCreateInvite} onRevoke={onRevokeInvite} />
      )}
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

/// Shown only when there are devices. An empty "nothing here" card on a screen opened twice a
/// year is a question with no answer: a browser has no devices.
function DevicesCard({ devices, onRevoke }: {
  devices: Device[]
  onRevoke: (id: number) => Promise<void>
}) {
  const [busyId, setBusyId] = useState<number | null>(null)

  return (
    <Card>
      <SectionTitle>Пристрої</SectionTitle>
      <p className="text-sm text-neutral-500">
        Телефон заходить не паролем, а власним ключем. Відкликаний ключ перестає працювати
        одразу — але тільки на тому пристрої, решта лишаються.
      </p>
      <ul className="space-y-2">
        {devices.map((d) => (
          <li key={d.id} className="flex items-center gap-3">
            <div className="flex-1 min-w-0">
              <p className="font-medium truncate">{d.name}</p>
              <p className="text-xs text-neutral-400">
                {d.lastUsedAt
                  ? `востаннє ${dayMonth(d.lastUsedAt)}`
                  : `додано ${dayMonth(d.createdAt)}, ще не заходив`}
              </p>
            </div>
            <button
              onClick={() => { setBusyId(d.id); void onRevoke(d.id).finally(() => setBusyId(null)) }}
              disabled={busyId === d.id}
              className="text-sm text-red-600 px-2 disabled:opacity-40"
            >
              {busyId === d.id ? '…' : 'Відкликати'}
            </button>
          </li>
        ))}
      </ul>
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

/// Letting somebody else onto this instance. Shown to the owner only — and refused by the
/// server for anyone else regardless, because a screen deciding who may do what is a screen
/// that can be lied to.
///
/// The link is readable exactly once, here, right after it is made: the server keeps only a
/// hash of the code, the same way it treats a device token. A lost link is replaced, not
/// looked up — which is why the copy button is large and the list below never shows a code.
function InvitesCard({ invites, onCreate, onRevoke }: {
  invites: Invite[]
  onCreate: (note: string) => Promise<NewInvite>
  onRevoke: (id: number) => Promise<void>
}) {
  const [note, setNote] = useState('')
  const [fresh, setFresh] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const link = fresh ? `${window.location.origin}/?invite=${fresh}` : null

  async function create() {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      const made = await onCreate(note.trim())
      setFresh(made.code)
      setCopied(false)
      setNote('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Не вдалося створити запрошення')
    } finally {
      setBusy(false)
    }
  }

  async function copy() {
    if (!link) return
    try {
      await navigator.clipboard.writeText(link)
      setCopied(true)
    } catch {
      // Clipboard access can be refused; the link is on screen and selectable either way.
      setCopied(false)
    }
  }

  return (
    <Card>
      <SectionTitle>Запросити людину</SectionTitle>
      <p className="text-sm text-neutral-500">
        Той, хто перейде за посиланням, заведе собі окремий акаунт. Ваші гроші не
        перетинаються: ти не бачиш його записів, він не бачить твоїх.
      </p>

      {link ? (
        <div className="space-y-2 rounded-xl bg-neutral-100 dark:bg-neutral-800 p-3">
          <p className="text-xs text-neutral-500">
            Скопіюй зараз — після виходу з екрана посилання не показати повторно.
          </p>
          <p className="break-all text-xs font-mono">{link}</p>
          <div className="flex gap-2">
            <PrimaryButton onClick={() => void copy()}>
              {copied ? 'Скопійовано' : 'Скопіювати посилання'}
            </PrimaryButton>
            <button
              onClick={() => setFresh(null)}
              className="rounded-xl bg-white dark:bg-neutral-900 px-3 py-2 text-sm text-neutral-500"
            >
              Готово
            </button>
          </div>
        </div>
      ) : (
        <div className="flex gap-2">
          <input
            value={note}
            placeholder="Кого запрошуєш — «Оля»"
            maxLength={60}
            onChange={(e) => setNote(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') void create() }}
            className="flex-1 rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2.5"
          />
          <PrimaryButton onClick={() => void create()} disabled={busy}>
            {busy ? 'Роблю…' : 'Створити'}
          </PrimaryButton>
        </div>
      )}

      <FormError>{error}</FormError>

      {invites.length > 0 && (
        <ul className="space-y-2">
          {invites.map((i) => (
            <li key={i.id} className="flex items-baseline justify-between gap-3 text-sm">
              <span>
                {i.note || 'Без імені'}
                <span className="block text-xs text-neutral-400">
                  {i.usedByEmail
                    ? `Прийняв ${i.usedByEmail}`
                    : i.expired
                      ? 'Протерміноване'
                      : `Діє до ${dayMonth(i.expiresAt)}`}
                </span>
              </span>
              {/* A used invite stays: it is the only record of who was let in. */}
              {!i.usedByEmail && (
                <button
                  onClick={() => void onRevoke(i.id)}
                  className="text-xs text-red-600 shrink-0"
                >
                  Відкликати
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      <p className="text-xs text-neutral-400">
        Посилання одноразове й діє два тижні. Запрошувати може лише власник — той, кого ти
        запросиш, далі нікого покликати не зможе.
      </p>
    </Card>
  )
}
