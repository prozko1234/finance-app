import { Capacitor } from '@capacitor/core'
import { Preferences } from '@capacitor/preferences'

/// What changes when the app runs as a native shell instead of a browser tab.
///
/// Two things, and they are the same thing twice: the page no longer comes from the same
/// origin as the API, so a relative URL points nowhere and an HttpOnly cookie is a
/// third-party cookie that WKWebView drops. Hence an absolute base address and a device
/// token in a header.

const TOKEN_KEY = 'deviceToken'
const TOKEN_ID_KEY = 'deviceTokenId'

export function isNative(): boolean {
  return Capacitor.isNativePlatform()
}

/// Empty in the browser — the API is served from the same origin, and a relative `/api`
/// keeps working through the Vite proxy in development and the reverse proxy in production.
export function apiBase(): string {
  if (!isNative()) return ''

  const base = import.meta.env.VITE_API_BASE
  if (!base) {
    // Failing loudly beats a native build that silently calls its own bundle and shows an
    // empty screen no one can explain.
    throw new Error('VITE_API_BASE не заданий: нативна збірка не знає, де знаходиться API')
  }

  return base.replace(/\/+$/, '')
}

/// What this device will be called in the owner's device list.
export function deviceName(): string {
  return /iPad/i.test(navigator.userAgent) ? 'iPad' : 'iPhone'
}

/// Read once, then kept in memory: every API call needs it, and going through the native
/// bridge for each request would put an await on the critical path of the whole app.
let cached: string | null | undefined

export async function readDeviceToken(): Promise<string | null> {
  if (!isNative()) return null
  if (cached !== undefined) return cached

  const { value } = await Preferences.get({ key: TOKEN_KEY })
  cached = value
  return cached
}

export async function saveDeviceToken(id: number, token: string): Promise<void> {
  cached = token
  await Preferences.set({ key: TOKEN_KEY, value: token })
  await Preferences.set({ key: TOKEN_ID_KEY, value: String(id) })
}

export async function readDeviceTokenId(): Promise<number | null> {
  const { value } = await Preferences.get({ key: TOKEN_ID_KEY })
  const id = Number(value)
  return Number.isInteger(id) && id > 0 ? id : null
}

export async function forgetDeviceToken(): Promise<void> {
  cached = null
  await Preferences.remove({ key: TOKEN_KEY })
  await Preferences.remove({ key: TOKEN_ID_KEY })
}
