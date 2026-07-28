import type {
  AuthStatus, AppSettings, Budget, Category, SaveCategory, Recurring, SafeToSpend, SaveIncome, SaveRecurring, SaveTaxProfile, SaveTransaction,
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
      const body = await res.json()
      if (body?.error) message = body.error
    } catch {
      /* body is not JSON — keep the status message */
    }
    throw new Error(message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export const api = {
  getAuthStatus: () => http<AuthStatus>('/api/auth/me'),
  login: (password: string) =>
    http<void>('/api/auth/login', { method: 'POST', body: JSON.stringify({ password }) }),
  logout: () => http<void>('/api/auth/logout', { method: 'POST' }),

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

  getBudget: () => http<Budget>('/api/budget'),
  setBudget: (amount: number) =>
    http<Budget>('/api/budget', { method: 'PUT', body: JSON.stringify({ amount }) }),

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
