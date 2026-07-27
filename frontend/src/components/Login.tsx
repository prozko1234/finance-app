import { useState } from 'react'
import { Card, FormError, PrimaryButton } from './Screen'

/// The whole app behind one password. No email, no «forgot it», no registration — there is
/// one person here, and every field that is not the password is a field that does nothing.
export function Login({ onSubmit }: { onSubmit: (password: string) => Promise<void> }) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit() {
    if (!password || busy) return
    setBusy(true)
    setError(null)
    try {
      await onSubmit(password)
    } catch {
      // The server deliberately says only this much — a message that distinguished
      // "wrong password" from anything else would be a hint to whoever is guessing.
      setError('Невірний пароль')
      setPassword('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center px-4">
      <div className="w-full max-w-xs space-y-5">
        <div className="text-center space-y-1">
          <p className="text-2xl font-bold">finance</p>
          <p className="text-sm text-neutral-500">Знай одну цифру: скільки безпечно витратити сьогодні</p>
        </div>

        <Card>
          <label className="block space-y-2">
            <span className="text-sm text-neutral-500">Пароль</span>
            <input
              type="password"
              value={password}
              autoFocus
              autoComplete="current-password"
              onChange={(e) => setPassword(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void submit() }}
              className="w-full rounded-xl border border-neutral-200 dark:border-neutral-700 bg-transparent px-3 py-2.5"
            />
          </label>
          <FormError>{error}</FormError>
          <PrimaryButton onClick={() => void submit()} disabled={!password || busy}>
            {busy ? 'Заходжу…' : 'Увійти'}
          </PrimaryButton>
        </Card>
      </div>
    </div>
  )
}
