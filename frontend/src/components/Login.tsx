import { useState } from 'react'
import type { Credentials, Registration } from '../types'
import { Card, FormError, PrimaryButton } from './Screen'

/// The code from an invite link, or null for an ordinary visit. Read from the address rather
/// than typed: the link is the credential, and asking someone to copy a 43-character string
/// out of it by hand would be a way to lose them.
export function inviteCodeFromUrl(): string | null {
  const code = new URLSearchParams(window.location.search).get('invite')
  return code && code.trim() !== '' ? code : null
}

/// Signing in, or — arriving through an invite — creating the account first. There is no
/// open sign-up: without a code in the address this screen has no way to make an account,
/// which is the point. No forgotten-password flow either: no email is ever sent, the address
/// is only a login name.
export function Login({ invite, onSubmit, onRegister }: {
  invite: string | null
  onSubmit: (c: Credentials) => Promise<void>
  onRegister: (r: Registration) => Promise<void>
}) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const joining = invite !== null
  // Long enough that guessing is hopeless — the same figure the server enforces. Checked here
  // only so the button explains itself before the round trip, never instead of the server.
  const longEnough = password.length >= 10
  const ready = email.trim() !== '' && (joining ? longEnough : password !== '')

  async function submit() {
    if (!ready || busy) return
    setBusy(true)
    setError(null)
    try {
      if (joining) await onRegister({ code: invite, email: email.trim(), password })
      else await onSubmit({ email: email.trim(), password })
    } catch (e) {
      // Registering reports what the server said — "link already used", "account exists" —
      // because every one of those is something the person can act on. Signing in does not:
      // a message telling a wrong address from a wrong password is a hint to whoever is
      // guessing, and the server deliberately does not send one.
      setError(joining
        ? (e instanceof Error ? e.message : 'Не вдалося створити акаунт')
        : 'Невірна пошта або пароль')
      setPassword('')
    } finally {
      setBusy(false)
    }
  }

  const field = 'w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2.5'

  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <div className="w-full max-w-xs space-y-5">
        <div className="text-center space-y-1">
          <p className="text-2xl font-bold">finance</p>
          <p className="text-sm text-neutral-500">
            {joining
              ? 'Тебе запросили. Придумай пошту й пароль — це буде твій вхід.'
              : 'Знай одну цифру: скільки безпечно витратити сьогодні'}
          </p>
        </div>

        <Card>
          <label className="block space-y-2">
            <span className="text-sm text-neutral-500">Пошта</span>
            <input
              type="email"
              value={email}
              autoFocus
              autoComplete="username"
              inputMode="email"
              onChange={(e) => setEmail(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void submit() }}
              className={field}
            />
          </label>

          <label className="block space-y-2">
            <span className="text-sm text-neutral-500">Пароль</span>
            <input
              type="password"
              value={password}
              autoComplete={joining ? 'new-password' : 'current-password'}
              onChange={(e) => setPassword(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void submit() }}
              className={field}
            />
            {joining && (
              <span className="block text-xs text-neutral-400">
                {password === '' || longEnough
                  ? 'Не коротший за 10 символів. Довжина важить більше, ніж значки.'
                  : `Ще ${10 - password.length} символів`}
              </span>
            )}
          </label>

          <FormError>{error}</FormError>
          <PrimaryButton onClick={() => void submit()} disabled={!ready || busy}>
            {busy
              ? (joining ? 'Створюю…' : 'Заходжу…')
              : (joining ? 'Створити акаунт' : 'Увійти')}
          </PrimaryButton>

          {joining && (
            <p className="text-xs text-neutral-400">
              Твої гроші бачиш лише ти: дані кожного акаунта окремі, власник застосунку теж їх
              не бачить.
            </p>
          )}
        </Card>
      </div>
    </div>
  )
}
