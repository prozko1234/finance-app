import type {
  AuthStatus, AppSettings, Category, Credentials, Envelope, EnvelopePeriod, SaveEnvelope, SaveEnvelopeTarget, SaveCategory, Recurring, SafeToSpend, SaveIncome, SaveRecurring, SaveTaxProfile, SaveTransaction,
  Allocation, SaveAllocation, IncomePreview, OpeningBalance, SaveOpeningBalance, SaveSavingsEntry, SaveSavingsPlan, Savings, Stats, TaxDefaults, TaxProfile, Transaction,
} from './types'

/// Called whenever the server says "not you" — a cookie can expire mid-session, and the
/// screen must fall back to the login form instead of showing a broken dashboard.
let onUnauthorized: () => void = () => {}
export function setOnUnauthorized(handler: () => void) {
  onUnauthorized = handler
}

async function http<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  if (res.status === 401 && !url.startsWith('/api/auth')) onUnauthorized()
  if (!res.ok) {
    let message = `HTTP ${res.status}`
    try {
      // ProblemDetails (RFC 7807), which is what the API returns: the sentence written for
      // the user lives in `detail`. This used to look for `error`, a field the API has never
      // sent — so every failure reached the screen as "HTTP 400" and the actual reason
      // («У банці лише 240.00») was thrown away on the way.
      const body = await res.json()
      const detail = body?.detail ?? body?.title ?? body?.error
      if (typeof detail === 'string' && detail !== '') message = detail
    } catch {
      /* body is not JSON — keep the status message */
    }
    // Logged as well as thrown: a component may swallow the error into a red line the user
    // then screenshots, and the console is where the method, url and status still are.
    console.error(`API ${options?.method ?? 'GET'} ${url} → ${res.status}: ${message}`)
    throw new Error(message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export const api = {
  getAuthStatus: () => http<AuthStatus>('/api/auth/me'),
  login: (c: Credentials) =>
    http<void>('/api/auth/login', { method: 'POST', body: JSON.stringify(c) }),
  logout: () => http<void>('/api/auth/logout', { method: 'POST' }),
  changePassword: (currentPassword: string, newPassword: string) =>
    http<void>('/api/auth/password', {
      method: 'POST', body: JSON.stringify({ currentPassword, newPassword }),
    }),
  changeEmail: (password: string, email: string) =>
    http<void>('/api/auth/email', { method: 'POST', body: JSON.stringify({ password, email }) }),
  signOutEverywhere: () => http<void>('/api/auth/sign-out-everywhere', { method: 'POST' }),

  getCategories: () => http<Category[]>('/api/categories'),
  createCategory: (c: SaveCategory) =>
    http<Category>('/api/categories', { method: 'POST', body: JSON.stringify(c) }),
  updateCategory: (id: number, c: SaveCategory) =>
    http<Category>(`/api/categories/${id}`, { method: 'PUT', body: JSON.stringify(c) }),
  deleteCategory: (id: number) =>
    http<void>(`/api/categories/${id}`, { method: 'DELETE' }),

  getTransactions: (take = 20) => http<Transaction[]>(`/api/transactions?take=${take}`),
  createTransaction: (tx: SaveTransaction) =>
    http<Transaction>('/api/transactions', { method: 'POST', body: JSON.stringify(tx) }),
  createIncome: (i: SaveIncome) =>
    http<Transaction>('/api/transactions/income', { method: 'POST', body: JSON.stringify(i) }),
  updateTransaction: (id: number, tx: SaveTransaction) =>
    http<Transaction>(`/api/transactions/${id}`, { method: 'PUT', body: JSON.stringify(tx) }),
  deleteTransaction: (id: number) =>
    http<void>(`/api/transactions/${id}`, { method: 'DELETE' }),

  getOpeningBalance: () => http<OpeningBalance>('/api/opening-balance'),
  setOpeningBalance: (b: SaveOpeningBalance) =>
    http<OpeningBalance>('/api/opening-balance', { method: 'PUT', body: JSON.stringify(b) }),
  clearOpeningBalance: () => http<OpeningBalance>('/api/opening-balance', { method: 'DELETE' }),

  getSafeToSpend: () => http<SafeToSpend>('/api/summary/safe-to-spend'),

  getStats: (months: number, month: string | null) =>
    http<Stats>(`/api/stats?months=${months}${month ? `&month=${month}` : ''}`),

  getSettings: () => http<AppSettings>('/api/settings'),
  setDisplayCurrency: (currency: string) =>
    http<AppSettings>('/api/settings/currency', { method: 'PUT', body: JSON.stringify({ currency }) }),
  setPeriodStartDay: (day: number) =>
    http<AppSettings>('/api/settings/period-start-day', { method: 'PUT', body: JSON.stringify({ day }) }),

  getEnvelopeHistory: (id: number, periods = 6) =>
    http<EnvelopePeriod[]>(`/api/envelopes/${id}/history?periods=${periods}`),
  createEnvelope: (e: SaveEnvelope) =>
    http<Envelope>('/api/envelopes', { method: 'POST', body: JSON.stringify(e) }),
  updateEnvelope: (id: number, e: SaveEnvelope) =>
    http<Envelope>(`/api/envelopes/${id}`, { method: 'PUT', body: JSON.stringify(e) }),
  /// Ціль на банку: сума (в тій валюті, що показуємо) і, необовʼязково, дата.
  /// `amount: null` знімає ціль.
  setEnvelopeTarget: (id: number, t: SaveEnvelopeTarget) =>
    http<Envelope>(`/api/envelopes/${id}/target`, { method: 'PUT', body: JSON.stringify(t) }),
  /// Прибирає банку з очей, а не з історії: рухи в ній лишаються, тому це можливо лише
  /// для порожньої банки — сервер відповість сумою, якщо там ще щось є.
  deleteEnvelope: (id: number) =>
    http<void>(`/api/envelopes/${id}`, { method: 'DELETE' }),

  getRecurring: () => http<Recurring[]>('/api/recurring'),
  createRecurring: (r: SaveRecurring) =>
    http<Recurring>('/api/recurring', { method: 'POST', body: JSON.stringify(r) }),
  updateRecurring: (id: number, r: SaveRecurring) =>
    http<Recurring>(`/api/recurring/${id}`, { method: 'PUT', body: JSON.stringify(r) }),
  deleteRecurring: (id: number) =>
    http<void>(`/api/recurring/${id}`, { method: 'DELETE' }),

  getTaxProfile: () => http<TaxProfile>('/api/tax/profile'),
  saveTaxProfile: (p: SaveTaxProfile) =>
    http<TaxProfile>('/api/tax/profile', { method: 'PUT', body: JSON.stringify(p) }),
  getTaxDefaults: () => http<TaxDefaults>('/api/tax/defaults'),
  getSavings: () => http<Savings>('/api/savings'),
  saveSavingsPlan: (p: SaveSavingsPlan) =>
    http<Savings>('/api/savings/plan', { method: 'PUT', body: JSON.stringify(p) }),
  addSavingsEntry: (e: SaveSavingsEntry) =>
    http<Savings>('/api/savings/entries', { method: 'POST', body: JSON.stringify(e) }),
  deleteSavingsEntry: (id: number) =>
    http<Savings>(`/api/savings/entries/${id}`, { method: 'DELETE' }),
  updateSavingsEntry: (id: number, e: SaveSavingsEntry) =>
    http<Savings>(`/api/savings/entries/${id}`, { method: 'PUT', body: JSON.stringify(e) }),

  getAllocations: () => http<Allocation>('/api/allocations'),
  saveAllocation: (a: SaveAllocation) =>
    http<Allocation>('/api/allocations', { method: 'PUT', body: JSON.stringify(a) }),

  resetDevData: () => http<{ message: string }>('/api/dev/reset', { method: 'POST' }),
  seedDevData: () => http<{ message: string }>('/api/dev/seed', { method: 'POST' }),

  previewIncome: (amount: number, amountIncludesVat: boolean) =>
    http<IncomePreview>('/api/tax/income-preview', {
      method: 'POST',
      body: JSON.stringify({ amount, amountIncludesVat }),
    }),
}
