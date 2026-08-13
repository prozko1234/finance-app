/// Turning charge reminders on, from the browser's side.
///
/// Web Push needs three things to line up, and every one of them can fail in a way the user
/// cannot see: the server has to have VAPID keys, the browser has to grant permission, and on
/// iOS the app has to have been added to the Home Screen first — Safari refuses to subscribe a
/// page open in a tab, with an error that says nothing about why. So every step here reports
/// what went wrong in words, and the settings screen shows those words.
import { api } from './api'

export type PushProblem =
  | 'unsupported'
  | 'needs-install'
  | 'denied'
  | 'no-server-key'
  | 'failed'

/// Whether this browser can do Web Push at all, and whether it is in a state where it may.
export function pushSupported(): boolean {
  return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window
}

/// iOS only grants push to an installed PWA. Detected by the standalone display mode rather
/// than by sniffing the user agent: the rule is about how the app was opened, not about who
/// made the browser.
function installed(): boolean {
  return window.matchMedia('(display-mode: standalone)').matches
    || (navigator as { standalone?: boolean }).standalone === true
}

/// Asks permission and registers this device with the server. Returns null on success, or the
/// reason it could not be done.
export async function enablePush(): Promise<PushProblem | null> {
  if (!pushSupported()) return 'unsupported'

  const { publicKey } = await api.getPushKey()
  if (!publicKey) return 'no-server-key'

  // Asked before subscribing, because Safari's subscribe() on an uninstalled page throws
  // something unreadable, and "додай на екран «Додому»" is the only useful thing to say.
  if (!installed() && /iP(hone|ad|od)/.test(navigator.userAgent)) return 'needs-install'

  const permission = await Notification.requestPermission()
  if (permission !== 'granted') return 'denied'

  try {
    const registration = await navigator.serviceWorker.ready
    const subscription = await registration.pushManager.subscribe({
      // The only value any push service accepts today, and the only one that lets the payload
      // be encrypted for this browser alone.
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(publicKey),
    })

    const json = subscription.toJSON()
    if (!json.keys?.p256dh || !json.keys?.auth) return 'failed'

    await api.subscribePush({
      endpoint: subscription.endpoint,
      p256dh: json.keys.p256dh,
      auth: json.keys.auth,
    })
    return null
  } catch {
    return 'failed'
  }
}

/// Unsubscribes this device, on both sides. The server is told first: a browser that has
/// dropped its subscription can no longer tell anybody which endpoint it was.
export async function disablePush(): Promise<void> {
  if (!pushSupported()) return

  const registration = await navigator.serviceWorker.ready
  const subscription = await registration.pushManager.getSubscription()
  if (!subscription) return

  await api.unsubscribePush(subscription.endpoint).catch(() => {})
  await subscription.unsubscribe()
}

/// The VAPID key travels as URL-safe base64 and `applicationServerKey` wants raw bytes.
///
/// Returns the underlying ArrayBuffer rather than the view: TypeScript's `BufferSource` does
/// not accept a `Uint8Array<ArrayBufferLike>`, and every browser reads the buffer identically.
function urlBase64ToUint8Array(base64: string): ArrayBuffer {
  const padded = (base64 + '='.repeat((4 - (base64.length % 4)) % 4))
    .replace(/-/g, '+')
    .replace(/_/g, '/')

  const raw = atob(padded)
  const bytes = new Uint8Array(raw.length)
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i)
  return bytes.buffer
}
