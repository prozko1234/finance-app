import type { ImportRow } from './types'

/// Одна крамниця в прев'ю: усі її рядки, разом.
///
/// Це головна причина, чому імпорт на 300 рядків не є 300 рішеннями. Виписка за місяць — це
/// зазвичай 20–30 різних крамниць, і «ŻABKA × 14 · 340 zł» одним рядком з одним вибором
/// категорії лишає рівно стільки роботи, скільки її насправді є.
export interface ImportGroup {
  key: string
  merchant: string
  rows: ImportRow[]
  /// Сума по групі, зі знаком: видатки від'ємні, надходження додатні.
  total: number
  /// Куди покласти. null — ніхто не знає, і саме ці групи треба показати вгорі.
  categoryId: number | null
  include: boolean
}

/// Групи, готові до показу. Дублікати сюди не потрапляють — вони окремим списком, вимкнені
/// за замовчуванням: рядок, який уже є в застосунку, не має тихо додатись удруге.
export function groupRows(rows: ImportRow[]): ImportGroup[] {
  const byKey = new Map<string, ImportGroup>()

  for (const row of rows) {
    if (row.duplicateOfId !== null) continue

    // Рядок без назви крамниці групувати нема за чим — кожен такий сам по собі, інакше
    // безіменні перекази з різних місяців злиплись би в одну купу.
    const key = row.merchantKey || `line:${row.line}`
    const existing = byKey.get(key)

    if (existing) {
      existing.rows.push(row)
      existing.total += row.amount
      continue
    }

    byKey.set(key, {
      key,
      merchant: row.merchant || row.description || '—',
      rows: [row],
      total: row.amount,
      categoryId: row.suggestedCategoryId,
      include: true,
    })
  }

  // Невідомі — вгору: це єдині, що потребують рішення. Далі за розміром, бо саме там
  // помилка в категорії коштує найбільше.
  return [...byKey.values()].sort((a, b) => {
    if ((a.categoryId === null) !== (b.categoryId === null)) return a.categoryId === null ? -1 : 1
    return Math.abs(b.total) - Math.abs(a.total)
  })
}

/// Скільки груп іще чекають на рішення. Кнопка «Імпортувати» на це не зважає — можна
/// імпортувати і так, — але сказати про це треба.
export function undecidedCount(groups: ImportGroup[]): number {
  return groups.filter((g) => g.include && g.categoryId === null).length
}

/// Рядки, які поїдуть на сервер: тільки увімкнені групи, кожен рядок із категорією своєї
/// групи. Групи без категорії відпадають — сервер відмовив би все одно, а мовчазна втрата
/// половини імпорту виглядала б як успіх.
export function rowsToCommit(groups: ImportGroup[], extra: ImportRow[] = [], extraCategoryId: number | null = null) {
  const fromGroups = groups
    .filter((g) => g.include && g.categoryId !== null)
    .flatMap((g) => g.rows.map((r) => ({ row: r, categoryId: g.categoryId! })))

  const fromExtra = extraCategoryId === null
    ? []
    : extra.map((r) => ({ row: r, categoryId: extraCategoryId }))

  return [...fromGroups, ...fromExtra].map(({ row, categoryId }) => ({
    line: row.line,
    date: row.date,
    amount: row.amount,
    currency: row.currency,
    categoryId,
    note: row.description,
  }))
}
