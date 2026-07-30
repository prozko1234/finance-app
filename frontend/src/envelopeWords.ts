import type { BucketKind } from './types'

/// Як називати рух у банці й чим її малювати — залежно від того, що це за банка.
///
/// Раніше будь-який внесок у будь-яку банку звався однаково, а на кожній банці стояло 🐖: у
/// банці «Зобовʼязання» це читалось як помилка застосунку — гроші, які йдуть на борг, не
/// «відкладаються» в свинку-скарбничку. Слово має збігатися з тим, що людина насправді робить,
/// інакше воно щоразу вимагає перекладу в голові.
export interface EnvelopeWords {
  icon: string
  /// Підпис руху всередину в історії: «Внесок», «Погашення», «Інвестовано».
  deposit: string
  /// Кнопка, що кладе гроші: «Відкласти», «Погасити», «Інвестувати».
  depositAction: string
}

const WORDS: Record<BucketKind, EnvelopeWords> = {
  Savings: { icon: '🐖', deposit: 'Внесок', depositAction: 'Відкласти' },
  Investing: { icon: '📈', deposit: 'Інвестовано', depositAction: 'Інвестувати' },
  Debt: { icon: '🏦', deposit: 'Погашення', depositAction: 'Погасити' },
  Other: { icon: '📦', deposit: 'Внесок', depositAction: 'Відкласти' },
  // Гроші на витрати — це денна норма, а не банка; сюди можна дійти лише з чужих даних.
  Spending: { icon: '💳', deposit: 'Внесок', depositAction: 'Відкласти' },
}

export function envelopeWords(kind: BucketKind): EnvelopeWords {
  return WORDS[kind] ?? WORDS.Savings
}

export function envelopeIcon(kind: BucketKind): string {
  return envelopeWords(kind).icon
}

/// Зняття однакове для всіх видів: «зняв гроші з банки» — це те саме, що людина зробила, чи то
/// пенсія, чи борг. Вигадувати «вивести» й «повернути» означало б перекладати ще й це.
export const WITHDRAWAL_LABEL = 'Знято'
export const WITHDRAWAL_ACTION = 'Зняти'
export const WITHDRAWAL_ICON = '↩️'
