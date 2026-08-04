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
  /// Емодзі самої категорії. Раніше список угадував його з назви по табличці, тож усе, що
  /// користувач створив сам, показувалось однаковим 📦.
  categoryIcon?: string | null
  /// З якої банки заплачено. null — зі звичайних грошей на витрати.
  envelopeId?: number | null
  envelopeName?: string | null
  frequency: Frequency
  source: string
  /// Дохід: чи те, що вписали, було сумою з VAT. Відновлено з самого рядка — брутто й нетто
  /// різняться на цілий VAT, тож форма редагування відкривається на тому самому перемикачі.
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
  /// Звідки гроші: null (і за замовчуванням) — зі звичайних, інакше id банки.
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
  /// Завжди базова валюта: польський рушій рахує у злотих, і це ті цифри, що в книгової.
  currency: string
  /// Рік, на який перевірені вшиті ставки ZUS і пороги PIT.
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
  /// Назва схеми, яка диктує ціль, або null — тоді вирішує план. Якщо задано, редактор
  /// плану у формі нічого не змінить, і форма має це сказати.
  savingsFromScheme: string | null
}

export interface SavingsSummary {
  balance: number
  monthGoal: number
  depositedThisMonth: number
  stillToReserve: number
}

/// Одна банка: скільки в ній назбиралось і як іде цей місяць.
export interface EnvelopeSummary {
  id: number
  name: string
  kind: BucketKind
  /// Банка, яка існує завжди і яку годує план заощаджень.
  isDefault: boolean
  balance: number
  monthGoal: number
  depositedThisMonth: number
  stillToReserve: number
  /// Банку принесла схема: назву й ціль задає її кошик, тому тут їх не міняють.
  isFromScheme: boolean
  /// До чого банку наповнюють, якщо ціль поставили. Гроші не тримає — лише показує темп.
  target: EnvelopeTarget | null
}

/// «Відпустка 6 000 до червня» → «950 за період».
export interface EnvelopeTarget {
  amount: number
  /// Дата, до якої збираємо, або null — тоді темпу немає, є лише сума.
  date: string | null
  remaining: number
  periodsLeft: number
  /// Скільки має заходити щопері́од, щоб дійти вчасно. 0 — коли дати немає або ціль зібрана.
  perPeriod: number
  reached: boolean
  /// Дата минула, а грошей ще не досить — сказано вголос, а не перетворено в більшу суму.
  overdue: boolean
}

/// Ціль або її кінець: amount === null знімає ціль разом із датою.
export interface SaveEnvelopeTarget {
  amount: number | null
  currency?: string | null
  date?: string | null
}

/// Банка сама по собі, без цифр періоду — відповідь на створення й перейменування.
export interface Envelope {
  id: number
  name: string
  kind: BucketKind
  isDefault: boolean
}

/// Банка, зроблена руками: «Відпустка», «Ремонт». Вид — будь-який, крім `Spending`:
/// гроші на витрати — це денна норма, а не банка.
export interface SaveEnvelope {
  name: string
  kind: BucketKind
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
  envelopeId: number
  envelopeName: string
  /// Записав додаток, виконуючи схему, а не людина руками. Редагування чи видалення такого
  /// руху скасовується наступним завантаженням екрана, тому UI цього й не пропонує.
  isAuto: boolean
  /// Половина перекидання між банками. Редагується лише цілком: видалення забирає й другу
  /// половину, бо гроші, що пішли з однієї банки й не прийшли в жодну, — це не факт.
  isTransfer: boolean
}

/// Перекидання між банками: одна дія замість «зняти тут і не забути покласти там».
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
  /// Усі банки, не тільки заощадження — інакше в пенсійний нічого не покласти.
  envelopes: EnvelopeSummary[]
  /// Назва схеми розподілу, якщо ціль тепер диктує вона, а не план нижче.
  goalFromScheme: string | null
  /// День, коли порахували залишок, якщо саме це поставило план на паузу до наступної
  /// зарплати. Інакше null.
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
  /// У яку банку. Не вказано — у ту, що за замовчуванням.
  envelopeId?: number
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
  /// День, з якого рахуються витрати — 1 число або день, коли порахував залишок.
  windowStart: string | null
  /// Бюджет узятий із «скільки в мене зараз є», а не з доходу чи заданої суми.
  fromOpeningBalance: boolean
  /// Період, який покривають ці цифри: від дня зарплати до дня перед наступною.
  periodStart: string
  periodEnd: string
  /// Залишок минулого періоду, поки не сказано, куди його. null — питання вже закрите.
  carryover: Carryover | null
}

export interface Carryover {
  amount: number
  fromStart: string
  fromEnd: string
  /// Банка, куди піде відповідь за замовчуванням.
  envelopeName: string
}

export type CarryoverDecision = 'ToEnvelope' | 'ToBudget' | 'Ignore'

/// «Скільки в мене зараз є до кінця місяця» — старт не з 1 числа.
export interface OpeningBalance {
  isSet: boolean
  amount: number | null
  currency: string
  date: string | null
  /// Порахований цього місяця, тобто саме він зараз керує денною нормою.
  appliesNow: boolean
}

export interface SaveOpeningBalance {
  amount: number
  currency?: string
  date?: string
}

/// Одиниця повторення. Кварталу немає свідомо — це 3 × Month, як і пів року це 6 × Month.
export type RecurrenceUnit = 'Week' | 'Month' | 'Year'

export interface Recurring {
  id: number
  amountOriginal: number
  currencyOriginal: string
  categoryId: number
  categoryName: string
  /// Дата першого списання — від неї рахується весь розклад.
  startsOn: string
  unit: RecurrenceUnit
  interval: number
  active: boolean
  note?: string | null
  kind: 'Expense' | 'Income'
  amountIncludesVat: boolean
  /// Коли спишеться наступного разу; null — поки на паузі.
  nextChargeOn: string | null
  /// Цього періоду вже пішло з бюджету — рядок виглядає так само, тож це треба сказати.
  chargedThisPeriod: boolean
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
  /// Кожні N одиниць: 2 + Week — раз на два тижні, 3 + Month — раз на квартал.
  interval?: number
}

export interface AppSettings {
  /// Валюта, в якій користувач читає застосунок.
  displayCurrency: string
  /// Валюта зберігання. Не змінюється ніколи — історія не переписується.
  baseCurrency: string
  /// true = основна валюта не PLN, тож податковий розклад треба підписати окремо.
  taxesInBaseCurrency: boolean
  /// День місяця, коли приходять гроші — з нього починається період.
  periodStartDay: number
  /// Період, який цей день дає прямо зараз.
  periodStart: string
  periodEnd: string
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
  /// Скільки схема розподілу відклала в банки сама.
  savedByPlan: number
  /// Що додав або зняв руками, і що заплатив прямо з банки. Може бути від'ємним.
  savedByHand: number
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

/// required=false — локальна робота без акаунта; тоді екран входу не показуємо взагалі.
export interface AuthStatus {
  required: boolean
  authenticated: boolean
  /// Пошта акаунта; null, поки не увійшли.
  email: string | null
}

export interface Credentials {
  email: string
  password: string
}

/// Один період у житті банки: скільки в нього зайшло чи вийшло і що стало з балансом.
export interface EnvelopePeriod {
  start: string
  end: string
  moved: number
  balanceAfter: number
}

/// Один рядок виписки, як його зрозумів сервер. `amount` зі знаком: від'ємне — витрата.
export interface ImportRow {
  line: number
  date: string
  amount: number
  currency: string
  /// Рівно те, що написав банк — щоб рядок завжди можна було звірити з файлом.
  description: string
  /// Крамниця, прибрана для читання: без номера точки й банківських слів.
  merchant: string
  /// За чим групуються рядки однієї крамниці й на чому вчиться правило.
  merchantKey: string
  kind: 'Expense' | 'Income'
  /// Схожа транзакція, що вже є в застосунку — внесена руками чи імпортована раніше.
  duplicateOfId: number | null
  /// Куди цю крамницю клали минулого разу, або що каже вбудований словник. null — ніхто
  /// не знає, і тоді екран питає, а не вгадує.
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
  /// Що саме сервер розпізнав — щоб, коли імпорт виглядає дивно, було видно чому.
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
