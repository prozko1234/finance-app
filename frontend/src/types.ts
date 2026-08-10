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
  /// The same amount as the user reads it, at the rate of THIS transaction's own date.
  /// Equals amountBase while the base currency is PLN.
  amountDisplay: number
  displayCurrency: string
  fxRate: number
  fxDate: string
  categoryId: number
  categoryName: string
  /// The emoji the category itself carries. The list used to guess it from the name against a
  /// table, so everything the user made themselves showed the same 📦.
  categoryIcon?: string | null
  /// 'Pending' — a subscription charge the schedule wrote that nobody has confirmed yet.
  status?: 'Posted' | 'Pending'
  /// Which jar this was paid out of. Null — out of ordinary spending money.
  envelopeId?: number | null
  envelopeName?: string | null
  frequency: Frequency
  source: string
  /// Income only: whether the figure typed was the one with VAT. Recovered from the row —
  /// gross and net differ by the whole VAT — so the edit form opens on the same toggle.
  amountIncludesVat: boolean
  date: string
  merchant?: string | null
  note?: string | null
  createdAt: string
}

export interface SaveTransaction {
  amount: number
  currency: string
  categoryId: number
  frequency: Frequency
  date?: string | null
  merchant?: string | null
  note?: string | null
  /// Where the money comes from: null (the default) — ordinary money, otherwise a jar id.
  envelopeId?: number | null
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
  /// Always the base currency: the Polish engine works in złoty, and these are the figures
  /// the bookkeeper will see.
  currency: string
  /// The year the built-in ZUS rates and PIT thresholds were checked against.
  ratesYear: number
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
  /// The scheme dictating the goal, or null when the plan decides it. When set, the plan
  /// editor in the form changes nothing — and the form has to say so.
  savingsFromScheme: string | null
}

export interface SavingsSummary {
  balance: number
  monthGoal: number
  depositedThisMonth: number
  stillToReserve: number
}

/// One jar: what has piled up in it, and how this month is going.
export interface EnvelopeSummary {
  id: number
  name: string
  kind: BucketKind
  /// The jar that always exists and that the savings plan feeds.
  isDefault: boolean
  balance: number
  monthGoal: number
  depositedThisMonth: number
  stillToReserve: number
  /// The scheme brought this jar: its bucket owns the name and the goal, so neither is
  /// editable here.
  isFromScheme: boolean
  /// What the jar is being filled up to, if a target was set. Holds no money back — it only
  /// shows the pace.
  target: EnvelopeTarget | null
}

/// "Відпустка 6 000 до червня" → "950 за період".
export interface EnvelopeTarget {
  amount: number
  /// The date being saved towards, or null — then there is no pace, only an amount.
  date: string | null
  remaining: number
  periodsLeft: number
  /// What has to go in each period to arrive on time. 0 when there is no date, or it is met.
  perPeriod: number
  reached: boolean
  /// The date has gone by with money still missing — said out loud rather than quietly
  /// turned into a bigger monthly figure.
  overdue: boolean
}

/// A target, or the end of one: amount === null takes it off along with the date.
export interface SaveEnvelopeTarget {
  amount: number | null
  currency?: string | null
  date?: string | null
}

/// A jar on its own, without the period's figures — the answer to creating or renaming one.
export interface Envelope {
  id: number
  name: string
  kind: BucketKind
  isDefault: boolean
}

/// A jar made by hand: "Відпустка", "Ремонт". Any kind except `Spending` — money to spend
/// is the daily norm, not a jar.
export interface SaveEnvelope {
  name: string
  kind: BucketKind
}

export interface SavingsEntry {
  id: number
  date: string
  kind: 'Deposit' | 'Withdrawal'
  /// In base currency — what this movement did to the balance.
  amount: number
  /// What the person actually typed, and in which currency.
  amountOriginal: number
  currencyOriginal: string
  note: string | null
  envelopeId: number
  envelopeName: string
  /// Written by the app carrying out the scheme, not by hand. Editing or deleting such a
  /// movement is undone by the next screen load, which is why the UI does not offer it.
  isAuto: boolean
  /// Half of a transfer between jars. Edited only as a whole: deleting takes the other half
  /// too, because money that left one jar and arrived in none is not a fact.
  isTransfer: boolean
  /// Recorded as money that was already put away, so it never touched a period's budget.
  alreadySetAside: boolean
}

/// A transfer between jars: one act instead of "withdraw here and remember to deposit there".
export interface SaveTransfer {
  fromEnvelopeId: number
  toEnvelopeId: number
  amount: number
  currency?: string | null
  date?: string | null
  note?: string | null
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
  /// Every jar, not only the savings one — otherwise a pension pot has no way to be fed.
  envelopes: EnvelopeSummary[]
  /// The allocation scheme's name, when the goal is now its call rather than the plan's.
  goalFromScheme: string | null
  /// The day the balance was counted, when that is what stood the plan down until the next
  /// payday. Null otherwise.
  planPausedFrom: string | null
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
  /// Which jar. Omitted — the default one.
  envelopeId?: number
  /// Money that was ALREADY put away before it was written down. Joins the balance without
  /// being taken out of this period's budget — see SavingsEntry.AlreadySetAside.
  alreadySetAside?: boolean
}

export type BucketKind = 'Spending' | 'Savings' | 'Investing' | 'Debt' | 'Other'

export interface AllocationBucket {
  name: string
  kind: BucketKind
  percent: number
}

/// A bucket with the money that actually landed in it this month.
export interface BucketShare extends AllocationBucket {
  id: number
  amount: number
}

/// Where the budget went before the daily norm was worked out.
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

/// Either a preset key, or a name plus the user's own buckets.
export interface SaveAllocation {
  preset?: string | null
  name?: string | null
  buckets?: AllocationBucket[] | null
}

export interface SafeToSpend {
  date: string
  currency: string
  budgetSet: boolean
  periodBudget: number | null
  spentThisPeriod: number
  reservedRecurring: number
  remainingThisPeriod: number | null
  daysLeftInPeriod: number
  dailyNorm: number | null
  spentToday: number
  leftToday: number | null
  tomorrowIfStop: number | null
  tomorrowIfOnPlan: number | null
  monthTaxes: MonthTaxes | null
  envelopes: EnvelopeSummary[]
  allocation: AllocationSummary | null
  /// The day spending is counted from — the 1st, or the day the balance was counted.
  windowStart: string | null
  /// The budget came from "скільки в мене зараз є" rather than from income.
  fromOpeningBalance: boolean
  /// The period these figures cover: from payday to the day before the next one.
  periodStart: string
  periodEnd: string
  /// Last period's leftover, while it is still waiting to be placed. Null — already answered.
  carryover: Carryover | null
  /// What debts are holding back from this period. Separate from the recurring reserve so the
  /// home screen can name it: money missing with nothing explaining it is the whole complaint.
  reservedDebts: number
  /// Subscriptions whose day has come and gone without anybody saying they were paid.
  pendingCharges: PendingCharge[]
  /// The same money over a wider horizon: seven days from today, or fewer when the period ends
  /// first. Drives the day/week/period switch on the home card.
  daysThisWeek: number
  leftThisWeek: number | null
}

/// How far ahead the headline figure looks. Not three budgets — one, read at three scales.
export type Horizon = 'day' | 'week' | 'period'

/// What the month will ask for, whatever is in the account. Four lines in descending order of
/// how little say you have in them; only the last is a guess.
export interface MonthlyNeed {
  currency: string
  recurring: number
  jars: number
  debts: number
  /// Null until there are two whole months to take a median of — a figure invented from a
  /// fortnight is worse than no figure.
  typical: number | null
  total: number
  typicalKnown: boolean
}

/// A recurring charge waiting for «оплачено ✓». Its money is already held back from the daily
/// norm — confirming changes nothing that can be spent, it only stops the app asking.
export interface PendingCharge {
  transactionId: number
  name: string
  /// As entered, so it matches what the bank's page says («Netflix $15,99»)…
  amountOriginal: number
  currencyOriginal: string
  /// …and in the reading currency, which the rest of the screen is in.
  amountDisplay: number
  date: string
}

export interface Carryover {
  amount: number
  fromStart: string
  fromEnd: string
  /// The jar the default answer would put it in.
  envelopeName: string
}

export type CarryoverDecision = 'ToEnvelope' | 'ToBudget' | 'Ignore'

/// "Скільки в мене зараз є до кінця місяця" — starting somewhere other than the 1st.
export interface OpeningBalance {
  isSet: boolean
  amount: number | null
  currency: string
  date: string | null
  /// Counted this month, so it is what drives the daily norm right now.
  appliesNow: boolean
}

export interface SaveOpeningBalance {
  amount: number
  currency?: string
  date?: string
}

/// The unit a schedule repeats in. No quarter on purpose — that is 3 × Month, as half a year
/// is 6 × Month.
export type RecurrenceUnit = 'Week' | 'Month' | 'Year'

export interface Recurring {
  id: number
  amountOriginal: number
  currencyOriginal: string
  categoryId: number
  categoryName: string
  /// The date of the first charge — the whole schedule is counted from it.
  startsOn: string
  unit: RecurrenceUnit
  interval: number
  active: boolean
  note?: string | null
  kind: 'Expense' | 'Income'
  amountIncludesVat: boolean
  /// When it goes out next; null while paused.
  nextChargeOn: string | null
  /// Already taken out of this period's budget. The row looks identical either way, so it
  /// has to be said.
  chargedThisPeriod: boolean
  /// Its day has passed and nobody has said whether it went through. The money is held, not
  /// spent — the row is a question, and the home screen is where it gets answered.
  awaitingConfirmation?: boolean
}

export interface SaveRecurring {
  amount: number
  currency: string
  categoryId: number
  startsOn: string
  note?: string | null
  active: boolean
  /// Omitted = 'Expense'. 'Income' is a stable monthly salary or contract.
  kind?: 'Expense' | 'Income'
  amountIncludesVat?: boolean
  /// Omitted = 'Month'.
  unit?: RecurrenceUnit
  /// Every N units: 2 + Week is fortnightly, 3 + Month is quarterly.
  interval?: number
}

export interface AppSettings {
  /// The currency the user reads the app in.
  displayCurrency: string
  /// The storage currency. Never changes — history is not rewritten.
  baseCurrency: string
  /// True = the base currency is not PLN, so the tax split needs its own label.
  taxesInBaseCurrency: boolean
  /// The day of the month the money arrives — where the period starts.
  periodStartDay: number
  /// The period that day produces right now.
  periodStart: string
  periodEnd: string
}

/// The storage currency. Use it as the default when entering new amounts; to DISPLAY one,
/// take displayCurrency from the settings, or złoty end up labelled as something else.
export const BASE_CURRENCY = 'PLN'
export const CURRENCIES = ['PLN', 'UAH', 'USD', 'EUR'] as const

/// 'None' = "просто гроші": the whole amount is yours. The rest are Polish tax regimes.
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

/// Statistics: a column per month, plus one month broken down by category.
/// month is "yyyy-MM"; income is przychód, VAT excluded, exactly as the budget counts it.
export interface MonthStats {
  month: string
  income: number
  expense: number
  net: number
  /// What the allocation scheme moved into jars by itself.
  savedByPlan: number
  /// What the user added or took out by hand, and paid straight out of a jar. Can be negative.
  savedByHand: number
}

export interface CategoryStats {
  categoryId: number
  name: string
  icon: string | null
  amount: number
  percent: number
  /// How many purchases the amount is made of — "часто" and "дорого" are fixed differently.
  count: number
  /// The median of the three months before this one, at this month's rate. Null — not enough
  /// history to call anything typical, and nothing may be compared.
  typical: number | null
}

/// A one-tap shortcut on the home screen. days is the window it was counted over, so the
/// screen can name the period instead of just claiming "часті".
export interface FrequentCategory {
  categoryId: number
  name: string
  icon: string | null
  uses: number
  days: number
}

export interface Stats {
  currency: string
  months: MonthStats[]
  selectedMonth: string
  selectedExpense: number
  categories: CategoryStats[]
  /// What the jars hold RIGHT NOW — stock, not flow. The monthly figures leave out money
  /// recorded as already set aside, so without this the screen would add up to less than the
  /// jars actually hold.
  savedBalance: number
  /// What went in, in the currency it was put in and not converted. Null when it was all one
  /// currency — the total already says it.
  savedByCurrency: CurrencyAmount[] | null
}

export interface CurrencyAmount {
  currency: string
  amount: number
}

/// required=false — running locally with no account; then no login screen is shown at all.
export interface AuthStatus {
  required: boolean
  authenticated: boolean
  /// The account's email; null until signed in.
  email: string | null
  /// Whether this account may hand out invites. The server refuses regardless of what the
  /// screen decided to show, so this only controls whether the section appears.
  isOwner: boolean
}

/// An invite as the owner sees it in the list. The code is absent on purpose: it exists in
/// readable form for exactly one response, the one that created it.
export interface Invite {
  id: number
  note: string
  createdAt: string
  expiresAt: string
  /// Who used it, or null while it is still open.
  usedByEmail: string | null
  usedAt: string | null
  expired: boolean
}

/// The one response that carries a usable code.
export interface NewInvite {
  id: number
  code: string
}

export interface Registration {
  code: string
  email: string
  password: string
}

export interface Credentials {
  email: string
  password: string
}

/// One period in a jar's life: what moved in or out, and what the balance became.
export interface EnvelopePeriod {
  start: string
  end: string
  moved: number
  balanceAfter: number
}

/// One statement row as the server understood it. `amount` is signed: negative is an expense.
export interface ImportRow {
  line: number
  date: string
  amount: number
  currency: string
  /// Exactly what the bank wrote, so a row can always be checked against the file.
  description: string
  /// The shop tidied up for reading: no branch number, none of the bank's own words.
  merchant: string
  /// What one shop's rows are grouped by, and what a rule is learned on.
  merchantKey: string
  kind: 'Expense' | 'Income'
  /// A similar transaction the app already has — entered by hand or imported earlier.
  duplicateOfId: number | null
  /// Where this shop went last time, or what the built-in dictionary says. Null — nobody
  /// knows, and then the screen asks rather than guesses.
  suggestedCategoryId: number | null
}

export interface ImportProblem {
  line: number
  reason: string
  raw: string
}

export interface ImportPreview {
  rows: ImportRow[]
  problems: ImportProblem[]
  /// What the server actually recognised, so a strange-looking import shows its reasons.
  delimiter: string
  headerFound: boolean
  encoding: string
  columns: string[]
}

export interface ImportRowToSave {
  line: number
  date: string
  amount: number
  currency: string
  categoryId: number
  note?: string | null
}

export interface ImportResult {
  created: number
  failed: number
  problems: ImportProblem[]
}

// --- Debts, both ways round ---

export type DebtDirection = 'IOwe' | 'TheyOweMe'

/// Where the money came from — or, for money coming back, where it went. This is the whole
/// feature: a payment on a debt is an ordinary movement with a source, and all three sources
/// already existed in the app before debts had a screen of their own.
export type DebtPaymentSource = 'Spendable' | 'Envelope' | 'AlreadyHappened'

export interface DebtPayment {
  id: number
  date: string
  amount: number
  amountOriginal: number
  currencyOriginal: string
  source: DebtPaymentSource
  envelopeId: number | null
  envelopeName: string | null
  note: string | null
}

export interface Debt {
  id: number
  direction: DebtDirection
  person: string
  amount: number
  amountOriginal: number
  currencyOriginal: string
  date: string
  deadline: string | null
  reserveFromBudget: boolean
  paid: number
  /// What is still owed. The figure the card leads with, because it is the one that goes DOWN
  /// as the debt is paid — a balance that grew was what made the old jar read backwards.
  outstanding: number
  /// What this period is holding back for it, or 0 when nothing was asked for.
  perPeriod: number
  periodsLeft: number
  overdue: boolean
  closedOn: string | null
  note: string | null
  payments: DebtPayment[]
}

export interface Debts {
  currency: string
  iOweTotal: number
  theyOweMeTotal: number
  reservedThisPeriod: number
  iOwe: Debt[]
  theyOweMe: Debt[]
}

export interface SaveDebt {
  direction: DebtDirection
  person: string
  amount: number
  currency: string | null
  date: string | null
  deadline: string | null
  reserveFromBudget: boolean
  note: string | null
}

export interface SaveDebtPayment {
  amount: number
  currency: string | null
  date: string | null
  source: DebtPaymentSource
  envelopeId: number | null
  note: string | null
}
