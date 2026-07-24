export type Priority = 'Must' | 'Should' | 'Want'
export type Frequency = 'OneOff' | 'Recurring'

export interface Category {
  id: number
  name: string
  icon?: string | null
}

export interface Transaction {
  id: number
  amountOriginal: number
  currencyOriginal: string
  amountBase: number
  fxRate: number
  fxDate: string
  categoryId: number
  categoryName: string
  priority: Priority
  frequency: Frequency
  source: string
  date: string
  merchant?: string | null
  note?: string | null
  createdAt: string
}

export interface SaveTransaction {
  amount: number
  currency: string
  categoryId: number
  priority: Priority
  frequency: Frequency
  date?: string | null
  merchant?: string | null
  note?: string | null
}

export interface Budget {
  set: boolean
  monthlyAmount: number | null
  currency: string
  updatedAt: string | null
}

export interface SafeToSpend {
  date: string
  currency: string
  budgetSet: boolean
  monthlyBudget: number | null
  spentThisMonth: number
  reservedRecurring: number
  remainingThisMonth: number | null
  daysLeftInMonth: number
  safeToSpendToday: number | null
}

export interface Recurring {
  id: number
  amountOriginal: number
  currencyOriginal: string
  categoryId: number
  categoryName: string
  dayOfMonth: number
  active: boolean
  note?: string | null
}

export interface SaveRecurring {
  amount: number
  currency: string
  categoryId: number
  dayOfMonth: number
  note?: string | null
  active: boolean
}

export const BASE_CURRENCY = 'PLN'
export const CURRENCIES = ['PLN', 'UAH', 'USD', 'EUR'] as const
