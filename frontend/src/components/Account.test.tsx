import { describe, it, expect, vi, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Account } from './Account'

function props(over: { onChangePassword?: Mock; onChangeEmail?: Mock; onSignOutEverywhere?: Mock } = {}) {
  return {
    email: 'owner@finance.test',
    onChangePassword: over.onChangePassword
      ?? vi.fn<(c: string, n: string) => Promise<void>>().mockResolvedValue(undefined),
    onChangeEmail: over.onChangeEmail
      ?? vi.fn<(p: string, e: string) => Promise<void>>().mockResolvedValue(undefined),
    onSignOutEverywhere: over.onSignOutEverywhere
      ?? vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
    onLogout: vi.fn(),
    onBack: vi.fn(),
  }
}

describe('Account', () => {
  it('says which account you are in', () => {
    render(<Account {...props()} />)

    expect(screen.getByText(/owner@finance\.test/)).toBeInTheDocument()
  })

  /// The server refuses a short password anyway; refusing it here saves the round trip
  /// and, more to the point, says the rule before the user commits to it.
  it('will not send a new password shorter than the rule', async () => {
    const onChangePassword = vi.fn<(c: string, n: string) => Promise<void>>().mockResolvedValue(undefined)
    render(<Account {...props({ onChangePassword })} />)
    const user = userEvent.setup()

    await user.type(screen.getByLabelText('Поточний'), 'old password here')
    await user.type(screen.getByLabelText('Новий'), 'short')

    expect(screen.getByRole('button', { name: 'Змінити пароль' })).toBeDisabled()
    expect(onChangePassword).not.toHaveBeenCalled()
  })

  it('changes the password with both fields filled', async () => {
    const onChangePassword = vi.fn<(c: string, n: string) => Promise<void>>().mockResolvedValue(undefined)
    render(<Account {...props({ onChangePassword })} />)
    const user = userEvent.setup()

    await user.type(screen.getByLabelText('Поточний'), 'old password here')
    await user.type(screen.getByLabelText('Новий'), 'a longer new one')
    await user.click(screen.getByRole('button', { name: 'Змінити пароль' }))

    await waitFor(() => expect(onChangePassword).toHaveBeenCalledWith('old password here', 'a longer new one'))
  })

  it('shows what the server said when the current password is wrong', async () => {
    const onChangePassword = vi.fn<(c: string, n: string) => Promise<void>>()
      .mockRejectedValue(new Error('Поточний пароль невірний'))
    render(<Account {...props({ onChangePassword })} />)
    const user = userEvent.setup()

    await user.type(screen.getByLabelText('Поточний'), 'not it at all')
    await user.type(screen.getByLabelText('Новий'), 'a longer new one')
    await user.click(screen.getByRole('button', { name: 'Змінити пароль' }))

    expect(await screen.findByText('Поточний пароль невірний')).toBeInTheDocument()
  })

  /// Confirming with the password is what stops a left-open session from taking the
  /// account over by moving it to another address.
  it('needs the password to change the address', async () => {
    const onChangeEmail = vi.fn<(p: string, e: string) => Promise<void>>().mockResolvedValue(undefined)
    render(<Account {...props({ onChangeEmail })} />)
    const user = userEvent.setup()

    await user.clear(screen.getByLabelText('Пошта'))
    await user.type(screen.getByLabelText('Пошта'), 'new@home.test')
    expect(screen.getByRole('button', { name: 'Змінити пошту' })).toBeDisabled()

    await user.type(screen.getByLabelText('Пароль, щоб підтвердити'), 'old password here')
    await user.click(screen.getByRole('button', { name: 'Змінити пошту' }))

    await waitFor(() => expect(onChangeEmail).toHaveBeenCalledWith('old password here', 'new@home.test'))
  })

  it('closes every session on request', async () => {
    const onSignOutEverywhere = vi.fn<() => Promise<void>>().mockResolvedValue(undefined)
    render(<Account {...props({ onSignOutEverywhere })} />)
    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: 'Вийти з усіх пристроїв' }))

    await waitFor(() => expect(onSignOutEverywhere).toHaveBeenCalled())
  })
})
