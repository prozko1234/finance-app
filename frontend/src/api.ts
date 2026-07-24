import type { Budget, Category, SafeToSpend, SaveTransaction, Transaction } from './types'

async function http<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  })
  if (!res.ok) {
    let message = `HTTP ${res.status}`
    try {
      const body = await res.json()
      if (body?.error) message = body.error
    } catch {
      /* тіло не JSON — лишаємо статус */
    }
    throw new Error(message)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export const api = {
  getCategories: () => http<Category[]>('/api/categories'),

  getTransactions: (take = 20) => http<Transaction[]>(`/api/transactions?take=${take}`),
  createTransaction: (tx: SaveTransaction) =>
    http<Transaction>('/api/transactions', { method: 'POST', body: JSON.stringify(tx) }),
  deleteTransaction: (id: number) =>
    http<void>(`/api/transactions/${id}`, { method: 'DELETE' }),

  getBudget: () => http<Budget>('/api/budget'),
  setBudget: (amount: number) =>
    http<Budget>('/api/budget', { method: 'PUT', body: JSON.stringify({ amount }) }),

  getSafeToSpend: () => http<SafeToSpend>('/api/summary/safe-to-spend'),
}
