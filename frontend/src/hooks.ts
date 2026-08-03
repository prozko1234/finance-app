import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type {
  Credentials, SaveAllocation, SaveCategory, SaveEnvelope, SaveEnvelopeTarget, SaveIncome, SaveOpeningBalance, SaveRecurring, SaveSavingsEntry, SaveSavingsPlan, SaveTaxProfile, SaveTransaction, SaveTransfer,
} from './types'

export const queryKeys = {
  categories: ['categories'] as const,
  transactions: ['transactions'] as const,
  openingBalance: ['openingBalance'] as const,
  summary: ['summary'] as const,
  recurring: ['recurring'] as const,
  taxProfile: ['taxProfile'] as const,
  taxDefaults: ['taxDefaults'] as const,
  savings: ['savings'] as const,
  allocations: ['allocations'] as const,
  incomePreview: ['incomePreview'] as const,
  settings: ['settings'] as const,
  stats: ['stats'] as const,
  auth: ['auth'] as const,
  devices: ['devices'] as const,
}

export function useAuthStatus() {
  return useQuery({ queryKey: queryKeys.auth, queryFn: () => api.getAuthStatus(), retry: false })
}

/// Signing in makes every other query answerable, and they all failed while the door was
/// shut — so the cache is cleared rather than selectively invalidated.
export function useLogin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (c: Credentials) => api.login(c),
    onSuccess: () => qc.resetQueries(),
  })
}

/// Пароль змінено — сесія на цьому пристрої лишається живою, тому перечитуємо тільки
/// статус акаунта, а не весь кеш.
export function useChangePassword() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ current, next }: { current: string; next: string }) =>
      api.changePassword(current, next),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.auth }),
  })
}

export function useChangeEmail() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ password, email }: { password: string; email: string }) =>
      api.changeEmail(password, email),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.auth }),
  })
}

/// Виходить і цей пристрій теж — далі те саме прибирання кеша, що й при звичайному виході.
export function useSignOutEverywhere() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.signOutEverywhere(),
    onSuccess: () => qc.clear(),
  })
}

/// Пристрої, що заходять токеном — телефон і, згодом, віджет. У браузері цей список
/// порожній: браузер живе на куці й пристроєм себе не реєструє.
/// Імпорт чіпає майже все: транзакції, підсумок, статистику. Тому після нього — те саме
/// прибирання кеша, що й після будь-якого запису, а не точкове оновлення одного списку.
export function useImportPreview() {
  return useMutation({ mutationFn: (file: File) => api.previewImport(file) })
}

export function useCommitImport() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (rows: Parameters<typeof api.commitImport>[0]) => api.commitImport(rows),
    onSuccess: () => invalidate(),
  })
}

export function useDevices() {
  return useQuery({ queryKey: queryKeys.devices, queryFn: () => api.getDevices() })
}

export function useRevokeDevice() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => api.revokeDevice(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.devices }),
  })
}

export function useLogout() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.logout(),
    // Nothing cached may outlive the session: the next person to open this browser must
    // not see the balance flash on screen before the login form replaces it.
    onSuccess: () => qc.clear(),
  })
}

/// The month drives the query key: switching months is a new fetch, and the previously
/// seen month stays cached, so clicking back and forth on the chart does not flicker.
export function useStats(months: number, month: string | null, enabled = true) {
  return useQuery({
    queryKey: [...queryKeys.stats, months, month],
    queryFn: () => api.getStats(months, month),
    // Half a year of totals is not worth fetching on a screen that never shows them.
    enabled,
  })
}

export function useCategories() {
  return useQuery({ queryKey: queryKeys.categories, queryFn: () => api.getCategories() })
}

function useInvalidateCategories() {
  const qc = useQueryClient()
  return () => {
    qc.invalidateQueries({ queryKey: queryKeys.categories })
    qc.invalidateQueries({ queryKey: queryKeys.transactions })
    qc.invalidateQueries({ queryKey: queryKeys.recurring })
  }
}

export function useCreateCategory() {
  const invalidate = useInvalidateCategories()
  return useMutation({ mutationFn: (c: SaveCategory) => api.createCategory(c), onSuccess: invalidate })
}

export function useUpdateCategory() {
  const invalidate = useInvalidateCategories()
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: SaveCategory }) => api.updateCategory(id, data),
    onSuccess: invalidate,
  })
}

export function useDeleteCategory() {
  const invalidate = useInvalidateCategories()
  return useMutation({ mutationFn: (id: number) => api.deleteCategory(id), onSuccess: invalidate })
}

/// Скільки рядків просимо — у ключі, тож «Показати ще» це новий запит, а вже побачене
/// лишається в кеші. Інвалідація по префіксу ['transactions'] чіпає всі сторінки разом.
export function useTransactions(take = 20) {
  return useQuery({
    queryKey: [...queryKeys.transactions, take],
    queryFn: () => api.getTransactions(take),
  })
}

export function useSafeToSpend() {
  return useQuery({ queryKey: queryKeys.summary, queryFn: () => api.getSafeToSpend() })
}

export function useAllocations() {
  return useQuery({ queryKey: queryKeys.allocations, queryFn: () => api.getAllocations() })
}

/// Changing the scheme changes the daily norm and the savings goal, so both derived
/// queries have to go — not just the allocation itself.
export function useSettings() {
  return useQuery({ queryKey: queryKeys.settings, queryFn: () => api.getSettings() })
}

/// Так само, як валюта: день зарплати переставляє межі періоду, а отже й бюджет, денну
/// норму, резерв підписок і цілі банок. Дешевше перечитати все, ніж вгадувати.
export function useSetPeriodStartDay() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (day: number) => api.setPeriodStartDay(day),
    // Everything, not a hand-picked list. The list used to miss підписки (whose next charge
    // moves with the period boundary) and транзакції (whose «з цього періоду» labels do),
    // which is how half the screen changed its numbers and half did not.
    onSuccess: () => qc.invalidateQueries(),
  })
}

/// Currency touches every number on screen, so a switch invalidates everything that
/// carries money — not just the settings screen that made the change.
export function useSetDisplayCurrency() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (currency: string) => api.setDisplayCurrency(currency),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.settings })
      qc.invalidateQueries({ queryKey: queryKeys.summary })
      qc.invalidateQueries({ queryKey: queryKeys.transactions })
      qc.invalidateQueries({ queryKey: queryKeys.savings })
      qc.invalidateQueries({ queryKey: queryKeys.allocations })
    },
  })
}

/// Історія однієї банки. Id у ключі, тож перемикання між банками не мигає — уже
/// переглянутий лишається в кеші. Ключ починається з savings, тому будь-який рух грошей
/// (депозит, зняття) інвалідує й історію разом із рештою.
export function useEnvelopeHistory(envelopeId: number | null) {
  return useQuery({
    queryKey: [...queryKeys.savings, 'history', envelopeId],
    queryFn: () => api.getEnvelopeHistory(envelopeId!),
    enabled: envelopeId !== null,
  })
}

export function useSaveAllocation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (a: SaveAllocation) => api.saveAllocation(a),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.allocations })
      qc.invalidateQueries({ queryKey: queryKeys.summary })
      qc.invalidateQueries({ queryKey: queryKeys.savings })
    },
  })
}

export function useRecurring() {
  return useQuery({ queryKey: queryKeys.recurring, queryFn: () => api.getRecurring() })
}

/// After any write, the derived data (transactions, summary, budget, recurring) is stale.
function useInvalidate() {
  const qc = useQueryClient()
  return () => {
    qc.invalidateQueries({ queryKey: queryKeys.transactions })
    qc.invalidateQueries({ queryKey: queryKeys.summary })
    qc.invalidateQueries({ queryKey: queryKeys.recurring })
    // Every month cached under any key: a new expense changes the bar it lands in.
    qc.invalidateQueries({ queryKey: queryKeys.stats })
  }
}

export function useCreateTransaction() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (tx: SaveTransaction) => api.createTransaction(tx), onSuccess: invalidate })
}

export function useCreateIncome() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (i: SaveIncome) => api.createIncome(i), onSuccess: invalidate })
}

export function useUpdateTransaction() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: SaveTransaction }) => api.updateTransaction(id, data),
    onSuccess: invalidate,
  })
}

export function useUpdateIncome() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: SaveIncome }) => api.updateIncome(id, data),
    onSuccess: invalidate,
  })
}

export function useDeleteTransaction() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => api.deleteTransaction(id), onSuccess: invalidate })
}

export function useOpeningBalance() {
  return useQuery({ queryKey: queryKeys.openingBalance, queryFn: () => api.getOpeningBalance() })
}

/// Counting what is left changes the budget itself, so it invalidates everything the
/// ordinary money mutations do — plus the count.
function useInvalidateOpeningBalance() {
  const invalidate = useInvalidate()
  const qc = useQueryClient()
  return () => {
    invalidate()
    qc.invalidateQueries({ queryKey: queryKeys.openingBalance })
  }
}

export function useSetOpeningBalance() {
  const invalidate = useInvalidateOpeningBalance()
  return useMutation({
    mutationFn: (b: SaveOpeningBalance) => api.setOpeningBalance(b),
    onSuccess: invalidate,
  })
}

export function useClearOpeningBalance() {
  const invalidate = useInvalidateOpeningBalance()
  return useMutation({ mutationFn: () => api.clearOpeningBalance(), onSuccess: invalidate })
}

export function useCreateRecurring() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (r: SaveRecurring) => api.createRecurring(r), onSuccess: invalidate })
}

export function useUpdateRecurring() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: SaveRecurring }) => api.updateRecurring(id, data),
    onSuccess: invalidate,
  })
}

export function useDeleteRecurring() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => api.deleteRecurring(id), onSuccess: invalidate })
}

export function useTaxProfile() {
  return useQuery({ queryKey: queryKeys.taxProfile, queryFn: () => api.getTaxProfile() })
}

export function useTaxDefaults() {
  return useQuery({ queryKey: queryKeys.taxDefaults, queryFn: () => api.getTaxDefaults(), staleTime: Infinity })
}

export function useSaveTaxProfile() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (p: SaveTaxProfile) => api.saveTaxProfile(p),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.taxProfile }),
  })
}

/// Live income preview. Debounced so typing "24600" is one request, not five.
/// Only meaningful in the base currency — the tax engine works in PLN.
export function useIncomePreview(amount: number, includesVat: boolean, enabled: boolean) {
  const debounced = useDebounced(amount, 350)
  return useQuery({
    queryKey: [...queryKeys.incomePreview, debounced, includesVat],
    queryFn: () => api.previewIncome(debounced, includesVat),
    enabled: enabled && debounced > 0,
    staleTime: 60_000,
  })
}

function useDebounced<T>(value: T, ms: number): T {
  const [settled, setSettled] = useState(value)
  useEffect(() => {
    const id = setTimeout(() => setSettled(value), ms)
    return () => clearTimeout(id)
  }, [value, ms])
  return settled
}

export function useSavings() {
  return useQuery({ queryKey: queryKeys.savings, queryFn: api.getSavings })
}

/// Both the plan and manual entries change safe-to-spend, so the summary must refetch too —
/// and the income preview, which shows the savings goal while the form is still open.
function useSavingsMutation<T>(fn: (v: T) => Promise<unknown>) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: queryKeys.savings })
      qc.invalidateQueries({ queryKey: queryKeys.summary })
      qc.invalidateQueries({ queryKey: queryKeys.incomePreview })
    },
  })
}

export function useSaveSavingsPlan() {
  return useSavingsMutation((p: SaveSavingsPlan) => api.saveSavingsPlan(p))
}

export function useAddSavingsEntry() {
  return useSavingsMutation((e: SaveSavingsEntry) => api.addSavingsEntry(e))
}

export function useUpdateSavingsEntry() {
  return useSavingsMutation(({ id, data }: { id: number; data: SaveSavingsEntry }) =>
    api.updateSavingsEntry(id, data))
}

export function useTransferBetweenEnvelopes() {
  return useSavingsMutation((t: SaveTransfer) => api.transferBetweenEnvelopes(t))
}

export function useDeleteSavingsEntry() {
  return useSavingsMutation((id: number) => api.deleteSavingsEntry(id))
}

/// Банки як самостійна річ. Той самий інвалідатор, що й у рухів: список банок живе всередині
/// `savings`, а порожня банка все одно нічого не тримає з норми — але наступна може.
export function useCreateEnvelope() {
  return useSavingsMutation((e: SaveEnvelope) => api.createEnvelope(e))
}

export function useUpdateEnvelope() {
  return useSavingsMutation(({ id, data }: { id: number; data: SaveEnvelope }) =>
    api.updateEnvelope(id, data))
}

export function useSetEnvelopeTarget() {
  return useSavingsMutation(({ id, data }: { id: number; data: SaveEnvelopeTarget }) =>
    api.setEnvelopeTarget(id, data))
}

export function useDeleteEnvelope() {
  return useSavingsMutation((id: number) => api.deleteEnvelope(id))
}

/// Dev-only helpers. The endpoints exist only when the API runs in Development;
/// everything is refetched afterwards because every screen's data just changed.
export function useDevData() {
  const qc = useQueryClient()
  const run = (fn: () => Promise<{ message: string }>) => ({
    mutationFn: fn,
    onSuccess: () => qc.invalidateQueries(),
  })

  return {
    reset: useMutation(run(api.resetDevData)),
    seed: useMutation(run(api.seedDevData)),
  }
}
