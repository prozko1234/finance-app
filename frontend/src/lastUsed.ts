/// Remembers the last choices locally so the entry form opens pre-filled.
/// Kept in localStorage (not the server): it is a per-device UI preference,
/// and reading it must be instant — no round trip before the form renders.
import type { Horizon } from './types'

const KEY = 'finance:lastUsed'

/// How many income sources are worth keeping: a freelancer has a handful of clients,
/// and a longer list would stop being a shortcut and start being a search.
const MAX_INCOME_SOURCES = 5

export interface LastUsed {
  categoryId?: number
  currency?: string
  /// "Від кого / за що", most recent first. Income comes from the same few places,
  /// so retyping it every month is pure friction.
  incomeSources?: string[]
  /// Which scale the home card was last read at. A preference, not data: syncing it between
  /// devices would be a setting to manage, and the answer is one tap away on either.
  horizon?: Horizon
}

export function readHorizon(): Horizon {
  const saved = readLastUsed().horizon
  return saved === 'week' || saved === 'period' ? saved : 'day'
}

export function readIncomeSources(): string[] {
  return readLastUsed().incomeSources ?? []
}

/// Moves the source to the front, deduplicated case-insensitively — re-entering
/// "Faktura ACME" must not create a second entry next to "faktura acme".
export function rememberIncomeSource(source: string): void {
  const trimmed = source.trim()
  if (!trimmed) return

  const rest = readIncomeSources().filter((s) => s.toLowerCase() !== trimmed.toLowerCase())
  writeLastUsed({ incomeSources: [trimmed, ...rest].slice(0, MAX_INCOME_SOURCES) })
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
