import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Account } from './Account'
import { Login } from './Login'

function accountProps(over: Record<string, unknown> = {}) {
  return {
    email: 'bohdan@x.com',
    onChangePassword: vi.fn<(c: string, n: string) => Promise<void>>().mockResolvedValue(undefined),
    onChangeEmail: vi.fn<(p: string, e: string) => Promise<void>>().mockResolvedValue(undefined),
    onSignOutEverywhere: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
    onLogout: vi.fn(),
    onBack: vi.fn(),
    devices: [],
    onRevokeDevice: vi.fn().mockResolvedValue(undefined),
    isOwner: true,
    invites: [],
    onCreateInvite: vi.fn().mockResolvedValue({ id: 1, code: 'SECRET-CODE' }),
    onRevokeInvite: vi.fn().mockResolvedValue(undefined),
    ...over,
  }
}

describe('Invites', () => {
  /// The server refuses anyone else regardless, but a section offering something that will
  /// always fail is a section that reads as broken.
  it('is offered to the owner only', () => {
    const { rerender } = render(<Account {...accountProps({ isOwner: false })} />)
    expect(screen.queryByText('Запросити людину')).not.toBeInTheDocument()

    rerender(<Account {...accountProps({ isOwner: true })} />)
    expect(screen.getByText('Запросити людину')).toBeInTheDocument()
  })

  /// The code is readable exactly once — the server stores only its hash. So the one moment
  /// it exists on screen has to produce a link that can actually be sent.
  it('shows the full link once, after making it', async () => {
    render(<Account {...accountProps()} />)

    expect(screen.queryByText(/SECRET-CODE/)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Створити' }))

    expect(screen.getByText(new RegExp('\\?invite=SECRET-CODE'))).toBeInTheDocument()
    expect(screen.getByText(/після виходу з екрана посилання не показати повторно/i))
      .toBeInTheDocument()
  })

  /// A used invite is the only record of who was let in, so it is never revocable.
  it('offers to revoke an open invite but not a used one', () => {
    render(<Account {...accountProps({
      invites: [
        { id: 1, note: 'Оля', createdAt: '2026-08-01', expiresAt: '2026-08-15', usedByEmail: 'olya@x.com', usedAt: '2026-08-02', expired: false },
        { id: 2, note: 'Брат', createdAt: '2026-08-01', expiresAt: '2026-08-15', usedByEmail: null, usedAt: null, expired: false },
      ],
    })} />)

    expect(screen.getByText('Прийняв olya@x.com')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Відкликати' })).toHaveLength(1)
  })
})

describe('Login', () => {
  const handlers = {
    onSubmit: vi.fn<(c: { email: string; password: string }) => Promise<void>>().mockResolvedValue(undefined),
    onRegister: vi.fn<(r: { code: string; email: string; password: string }) => Promise<void>>().mockResolvedValue(undefined),
  }

  it('signs in when there is no invite in the address', () => {
    render(<Login invite={null} {...handlers} />)

    expect(screen.getByRole('button', { name: 'Увійти' })).toBeInTheDocument()
    expect(screen.queryByText(/Тебе запросили/)).not.toBeInTheDocument()
  })

  /// Arriving through a link is a different act with a different button, and the code comes
  /// from the address rather than being typed — it is 43 characters long.
  it('creates an account when arriving with a code', async () => {
    render(<Login invite="THE-CODE" {...handlers} />)

    expect(screen.getByText(/Тебе запросили/)).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText(/Пошта/), 'olya@x.com')
    await userEvent.type(screen.getByLabelText(/Пароль/), 'dovhyi-parol')
    await userEvent.click(screen.getByRole('button', { name: 'Створити акаунт' }))

    expect(handlers.onRegister).toHaveBeenCalledWith({
      code: 'THE-CODE', email: 'olya@x.com', password: 'dovhyi-parol',
    })
  })

  /// The server enforces the length; saying so before the round trip is the difference
  /// between a hint and a rejection.
  it('will not send a password the server would refuse', async () => {
    render(<Login invite="THE-CODE" {...handlers} />)

    await userEvent.type(screen.getByLabelText(/Пошта/), 'olya@x.com')
    await userEvent.type(screen.getByLabelText(/Пароль/), 'korotkyi')

    expect(screen.getByText(/Ще 2 символів/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Створити акаунт' })).toBeDisabled()
  })
})
