/// Remembers the last choices locally so the entry form opens pre-filled.
/// Kept in localStorage (not the server): it is a per-device UI preference,
/// and reading it must be instant — no round trip before the form renders.
const KEY = 'finance:lastUsed'

export interface LastUsed {
  categoryId?: number
  currency?: string
}

export function readLastUsed(): LastUsed {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? (JSON.parse(raw) as LastUsed) : {}
  } catch {
    return {}
  }
}

export function writeLastUsed(next: LastUsed): void {
  try {
    localStorage.setItem(KEY, JSON.stringify({ ...readLastUsed(), ...next }))
  } catch {
    /* private mode / quota — defaults simply stop being remembered */
  }
}
