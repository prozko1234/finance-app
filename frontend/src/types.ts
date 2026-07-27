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
  /// Та сама сума, як її читає користувач — за курсом дати САМОЇ транзакції.
  /// Дорівнює amountBase, поки основна валюта PLN.
  amountDisplay: number
  displayCurrency: string
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
  /// Завжди базова валюта: польський рушій рахує у злотих, і це ті цифри, що в книгової.
  currency: string
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
  savingsMode: 'Fixed' | 'Percent'
  savingsValue: number
  savingsActive: boolean
  savingsGoalAfter: number
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
  /// У базовій валюті — те, що ця операція зробила з балансом.
  amount: number
  /// Те, що людина реально вписала, і в якій валюті.
  amountOriginal: number
  currencyOriginal: string
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
  /// Назва схеми розподілу, якщо ціль тепер диктує вона, а не план нижче.
  goalFromScheme: string | null
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
  currency?: string | null
}

export type BucketKind = 'Spending' | 'Savings' | 'Investing' | 'Debt' | 'Other'

export interface AllocationBucket {
  name: string
  kind: BucketKind
  percent: number
}

/// Кошик із сумою, що реально в нього потрапила цього місяця.
export interface BucketShare extends AllocationBucket {
  id: number
  amount: number
}

/// Куди пішов бюджет ще до того, як порахувалась денна норма.
export interface AllocationSummary {
  schemeName: string
  preset: string | null
  spendable: number
  reserved: number
  buckets: BucketShare[]
}

export interface AllocationScheme {
  name: string
  preset: string | null
  buckets: AllocationBucket[]
}

export interface AllocationPreset {
  key: string
  name: string
  hint: string
  buckets: AllocationBucket[]
}

export interface Allocation {
  active: AllocationScheme
  presets: AllocationPreset[]
}

/// Або ключ пресета, або назва плюс власні кошики.
export interface SaveAllocation {
  preset?: string | null
  name?: string | null
  buckets?: AllocationBucket[] | null
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
  allocation: AllocationSummary | null
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
  kind: 'Expense' | 'Income'
  amountIncludesVat: boolean
}

export interface SaveRecurring {
  amount: number
  currency: string
  categoryId: number
  dayOfMonth: number
  note?: string | null
  active: boolean
  /// Omitted = 'Expense'. 'Income' is a stable monthly salary or contract.
  kind?: 'Expense' | 'Income'
  amountIncludesVat?: boolean
}

export interface AppSettings {
  /// Валюта, в якій користувач читає застосунок.
  displayCurrency: string
  /// Валюта зберігання. Не змінюється ніколи — історія не переписується.
  baseCurrency: string
  /// true = основна валюта не PLN, тож податковий розклад треба підписати окремо.
  taxesInBaseCurrency: boolean
}

/// Валюта зберігання. Для вводу нових сум — дефолт; для показу бери displayCurrency
/// з налаштувань, інакше підпишеш злоті чужою міткою.
export const BASE_CURRENCY = 'PLN'
export const CURRENCIES = ['PLN', 'UAH', 'USD', 'EUR'] as const

/// 'None' = «просто гроші»: сума вся твоя. Решта — форми оподаткування в Польщі.
export type TaxRegime = 'None' | 'Ryczalt' | 'UoP' | 'Zlecenie'

export interface TaxProfile {
  regime: TaxRegime
  ryczaltRate: number
  vatPayer: boolean
  vatRate: number
  zusType: string
  zusSocial: number
  healthContribution: number
  chorobowe: boolean
  studentUnder26: boolean
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

/// Статистика: стовпчики по місяцях + розклад одного місяця по категоріях.
/// month — "yyyy-MM"; income для доходу — це przychód без VAT, як і в бюджеті.
export interface MonthStats {
  month: string
  income: number
  expense: number
  net: number
}

export interface CategoryStats {
  categoryId: number
  name: string
  icon: string | null
  amount: number
  percent: number
  count: number
}

export interface Stats {
  currency: string
  months: MonthStats[]
  selectedMonth: string
  selectedExpense: number
  categories: CategoryStats[]
}
