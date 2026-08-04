import type { BucketKind } from './types'

/// What to call a movement in a jar, and what to draw it with — depending on the kind of jar.
///
/// Every deposit into every jar used to be called the same thing, and every jar wore a 🐖: in a
/// jar called "Зобовʼязання" that read as a bug — money going towards a debt is not being
/// saved into a piggy bank. The word has to match what the person is actually doing, or it
/// needs translating in one's head every time.
export interface EnvelopeWords {
  icon: string
  /// The label for money going in, in the history: "Внесок", "Погашення", "Інвестовано".
  deposit: string
  /// The button that puts money in: "Відкласти", "Погасити", "Інвестувати".
  depositAction: string
}

const WORDS: Record<BucketKind, EnvelopeWords> = {
  Savings: { icon: '🐖', deposit: 'Внесок', depositAction: 'Відкласти' },
  Investing: { icon: '📈', deposit: 'Інвестовано', depositAction: 'Інвестувати' },
  Debt: { icon: '🏦', deposit: 'Погашення', depositAction: 'Погасити' },
  Other: { icon: '📦', deposit: 'Внесок', depositAction: 'Відкласти' },
  // Money to spend is the daily norm, not a jar; this is only reachable from foreign data.
  Spending: { icon: '💳', deposit: 'Внесок', depositAction: 'Відкласти' },
}

export function envelopeWords(kind: BucketKind): EnvelopeWords {
  return WORDS[kind] ?? WORDS.Savings
}

export function envelopeIcon(kind: BucketKind): string {
  return envelopeWords(kind).icon
}

/// A withdrawal is the same for every kind: taking money out of a jar is the same act whether
/// it is a pension or a debt. Inventing separate verbs would be one more thing to translate.
export const WITHDRAWAL_LABEL = 'Знято'
export const WITHDRAWAL_ACTION = 'Зняти'
export const WITHDRAWAL_ICON = '↩️'
