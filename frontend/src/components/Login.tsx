import { useState } from 'react'
import type { Credentials } from '../types'
import { Card, FormError, PrimaryButton } from './Screen'

/// Signing in to one's own account. There is no sign-up — there is one account and it already
/// exists; there is no forgotten-password flow either: no email is ever sent, the address is
/// only a login name.
export function Login({ onSubmit }: { onSubmit: (c: Credentials) => Promise<void> }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const ready = email.trim() !== '' && password !== ''

  async function submit() {
    if (!ready || busy) return
    setBusy(true)
    setError(null)
    try {
      await onSubmit({ email: email.trim(), password })
    } catch {
      // The server deliberately says only this much — a message that distinguished a
      // wrong address from a wrong password would be a hint to whoever is guessing.
      setError('Невірна пошта або пароль')
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
          <p className="text-sm text-neutral-500">Знай одну цифру: скільки безпечно витратити сьогодні</p>
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
              autoComplete="current-password"
              onChange={(e) => setPassword(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void submit() }}
              className={field}
            />
          </label>

          <FormError>{error}</FormError>
          <PrimaryButton onClick={() => void submit()} disabled={!ready || busy}>
            {busy ? 'Заходжу…' : 'Увійти'}
          </PrimaryButton>
        </Card>
      </div>
    </div>
  )
}
