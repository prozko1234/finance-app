export type Priority = 'Must' | 'Should' | 'Want'
export type Frequency = 'OneOff' | 'Recurring'

export interface Category {
  id: number
  name: string
  icon?: string | null
  color?: string | null
  sortOrder: number
  isSystem: boolean
}

export interface SaveCategory {
  name: string
  icon?: string | null
  color?: string | null
}

export interface Transaction {
  id: number
  kind: 'Expense' | 'Income'
  grossWithVat?: number | null
  vatAmount?: number | null
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

/// Local YYYY-MM-DD (never UTC — a late-evening entry must not jump to tomorrow).
export function todayIso(): string {
  const d = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export function shiftIso(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(y, m - 1, d + days)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${dt.getFullYear()}-${pad(dt.getMonth() + 1)}-${pad(dt.getDate())}`
}

export interface SaveIncome {
  amount: number
  amountIncludesVat: boolean
  currency: string
  date?: string | null
  note?: string | null
}

export interface Budget {
  set: boolean
  monthlyAmount: number | null
  currency: string
  updatedAt: string | null
}

/// Explains the gap between what landed on the account and the month's budget.
export interface MonthTaxes {
  gross: number
  revenue: number
  vat: number
  zusSocial: number
  health: number
  tax: number
  setAside: number
  takeHome: number
}

/// Live feedback while typing an invoice: what it adds to THIS month's budget.
/// A monthly delta, not a per-invoice figure — contributions are monthly.
export interface IncomePreview {
  invoiceGross: number
  invoiceVat: number
  invoiceRevenue: number
  budgetBefore: number
  budgetAfter: number
  budgetDelta: number
  isFirstIncomeThisMonth: boolean
  monthAfter: MonthTaxes
  currency: string
}

export interface SavingsSummary {
  balance: number
  monthGoal: number
  depositedThisMonth: number
  stillToReserve: number
}

export interface SavingsEntry {
  id: number
  date: string
  kind: 'Deposit' | 'Withdrawal'
  amount: number
  note: string | null
}

export interface Savings {
  mode: 'Fixed' | 'Percent'
  value: number
  active: boolean
  balance: number
  monthGoal: number
  depositedThisMonth: number
  stillToReserve: number
  currency: string
  recent: SavingsEntry[]
}

export interface SaveSavingsPlan {
  mode: 'Fixed' | 'Percent'
  value: number
  active: boolean
}

export interface SaveSavingsEntry {
  kind: 'Deposit' | 'Withdrawal'
  amount: number
  date?: string | null
  note?: string | null
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
  dailyNorm: number | null
  spentToday: number
  leftToday: number | null
  tomorrowIfStop: number | null
  tomorrowIfOnPlan: number | null
  monthTaxes: MonthTaxes | null
  savings: SavingsSummary
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

export interface TaxProfile {
  regime: string
  ryczaltRate: number
  vatPayer: boolean
  vatRate: number
  zusType: string
  zusSocial: number
  healthContribution: number
  chorobowe: boolean
  validFrom: string
  monthlyContributionsTotal: number
}

export type SaveTaxProfile = Omit<TaxProfile, 'validFrom' | 'monthlyContributionsTotal'>

export interface TaxDefaults {
  year: number
  duzyWithChorobowe: number
  duzyWithoutChorobowe: number
  preferencyjnyWithChorobowe: number
  preferencyjnyWithoutChorobowe: number
  healthUnder60k: number
  health60kTo300k: number
  healthOver300k: number
}

export interface TakeHome {
  grossWithVat: number
  vatAmount: number
  revenue: number
  zusSocial: number
  healthContribution: number
  healthDeducted: number
  taxBase: number
  tax: number
  takeHome: number
  currency: string
}
