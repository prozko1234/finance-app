import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type {
  CarryoverDecision, Credentials, Registration, SaveAllocation, SaveCategory, SaveDebt, SaveDebtPayment, SaveEnvelope, SaveEnvelopeTarget, SaveIncome, SaveOpeningBalance, SaveRecurring, SaveSavingsEntry, SaveSavingsPlan, SaveTaxProfile, SaveTaxActuals, SaveTransaction, SaveTransfer, SpendWindow,
} from './types'

export const queryKeys = {
  categories: ['categories'] as const,
  frequentCategories: ['categories', 'frequent'] as const,
  transactions: ['transactions'] as const,
  openingBalance: ['openingBalance'] as const,
  summary: ['summary'] as const,
  recurring: ['recurring'] as const,
  monthlyNeed: ['monthlyNeed'] as const,
  taxProfile: ['taxProfile'] as const,
  taxDefaults: ['taxDefaults'] as const,
  taxActuals: ['taxActuals'] as const,
  savings: ['savings'] as const,
  debts: ['debts'] as const,
  allocations: ['allocations'] as const,
  incomePreview: ['incomePreview'] as const,
  settings: ['settings'] as const,
  stats: ['stats'] as const,
  auth: ['auth'] as const,
  invites: ['invites'] as const,
  devices: ['devices'] as const,
}

/// Any write re-reads everything, rather than a hand-picked list of keys.
///
/// Every figure in this app is derived from the same money: income sets the budget, the budget
/// sets the daily norm, the envelope goals and the recurring reserve; an expense paid out of a
/// jar changes its balance; the tax profile changes take-home and therefore all of the above;
/// the display currency changes every number on every screen, statistics included. Hand-picked
/// lists lost here again and again — the key that got forgotten was always the one that left
/// half the screen updated and half of it stale (useSetPeriodStartDay reached this same
/// conclusion first).
///
/// The price is a few extra GETs per action, and only for queries currently on screen:
/// TanStack Query refetches active ones only. For a solo app that is cheaper than the whole
/// class of "I changed it and nothing moved" bugs.
function useInvalidateEverything() {
  const qc = useQueryClient()
  return () => { qc.invalidateQueries() }
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

/// The password changed but this device's session stays alive, so only the account status is
/// re-read rather than the whole cache.
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

/// This device is signed out too, so the cache is emptied exactly as on an ordinary log out.
export function useSignOutEverywhere() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.signOutEverywhere(),
    onSuccess: () => qc.clear(),
  })
}

/// A preview writes nothing, so nothing is invalidated; committing the rows does.
export function useImportPreview() {
  return useMutation({ mutationFn: (file: File) => api.previewImport(file) })
}

export function useCommitImport() {
  const invalidate = useInvalidateEverything()
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

/// The window is part of the key, so switching between a week and a fortnight is a new fetch
/// and the one already seen stays cached.
export function useRecentSpending(window: SpendWindow, enabled = true) {
  return useQuery({
    queryKey: [...queryKeys.stats, 'recent', window],
    queryFn: () => api.getRecentSpending(window),
    enabled,
  })
}

export function useInvites(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.invites,
    queryFn: () => api.getInvites(),
    enabled,
  })
}

export function useCreateInvite() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (note: string) => api.createInvite(note),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.invites }),
  })
}

export function useRevokeInvite() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => api.revokeInvite(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: queryKeys.invites }),
  })
}

export function useRegister() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (r: Registration) => api.register(r),
    onSuccess: () => qc.invalidateQueries(),
  })
}

export function useFrequentCategories() {
  return useQuery({
    queryKey: queryKeys.frequentCategories,
    queryFn: () => api.getFrequentCategories(),
  })
}

export function useCategories() {
  return useQuery({ queryKey: queryKeys.categories, queryFn: () => api.getCategories() })
}

/// Where money comes FROM. Its own query rather than a filter over the full list, because the
/// server tops the set up on first ask for accounts made before income had categories.
export function useIncomeCategories() {
  return useQuery({
    queryKey: [...queryKeys.categories, 'income'],
    queryFn: () => api.getCategories('Income'),
  })
}

export function useCreateCategory() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (c: SaveCategory) => api.createCategory(c), onSuccess: invalidate })
}

export function useUpdateCategory() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: SaveCategory }) => api.updateCategory(id, data),
    onSuccess: invalidate,
  })
}

export function useDeleteCategory() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (id: number) => api.deleteCategory(id), onSuccess: invalidate })
}

/// How many rows we ask for is part of the key, so "show more" is a new fetch while what
/// has already been seen stays cached. Invalidating the ['transactions'] prefix takes every
/// page with it.
export function useTransactions(take = 20) {
  return useQuery({
    queryKey: [...queryKeys.transactions, take],
    queryFn: () => api.getTransactions(take),
  })
}

/// Where last period's leftover goes. Answering it changes the budget, the daily norm and
/// possibly a jar balance, so it re-reads everything like any other money decision.
export function useDecideCarryover() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: (decision: CarryoverDecision) => api.decideCarryover(decision),
    onSuccess: invalidate,
  })
}

export function useSafeToSpend() {
  return useQuery({ queryKey: queryKeys.summary, queryFn: () => api.getSafeToSpend() })
}

export function useAllocations() {
  return useQuery({ queryKey: queryKeys.allocations, queryFn: () => api.getAllocations() })
}

export function useSettings() {
  return useQuery({ queryKey: queryKeys.settings, queryFn: () => api.getSettings() })
}

/// Payday moves the period boundaries, and with them the budget, the daily norm, the
/// recurring reserve and every envelope goal.
export function useSetPeriodStartDay() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (day: number) => api.setPeriodStartDay(day), onSuccess: invalidate })
}

/// The display currency is carried by every screen that shows money, statistics included —
/// which is precisely the key the hand-picked list used to forget.
export function useSetDisplayCurrency() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: (currency: string) => api.setDisplayCurrency(currency),
    onSuccess: invalidate,
  })
}

/// One envelope's history. The id is in the key, so switching between jars does not flicker —
/// the one already looked at stays cached.
export function useEnvelopeHistory(envelopeId: number | null) {
  return useQuery({
    queryKey: [...queryKeys.savings, 'history', envelopeId],
    queryFn: () => api.getEnvelopeHistory(envelopeId!),
    enabled: envelopeId !== null,
  })
}

export function useSaveAllocation() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (a: SaveAllocation) => api.saveAllocation(a), onSuccess: invalidate })
}

export function useTaxActuals() {
  return useQuery({ queryKey: queryKeys.taxActuals, queryFn: () => api.getTaxActuals() })
}

/// Correcting a contribution changes the month's budget, the daily norm and everything hanging
/// off it — so this invalidates the world like every other write that moves money.
export function useSaveTaxActuals() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: (a: SaveTaxActuals) => api.saveTaxActuals(a),
    onSuccess: invalidate,
  })
}

export function useMonthlyNeed() {
  return useQuery({ queryKey: queryKeys.monthlyNeed, queryFn: () => api.getMonthlyNeed() })
}

export function useRecurring() {
  return useQuery({ queryKey: queryKeys.recurring, queryFn: () => api.getRecurring() })
}

export function useCreateTransaction() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (tx: SaveTransaction) => api.createTransaction(tx), onSuccess: invalidate })
}

export function useCreateIncome() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (i: SaveIncome) => api.createIncome(i), onSuccess: invalidate })
}

export function useUpdateTransaction() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: SaveTransaction }) => api.updateTransaction(id, data),
    onSuccess: invalidate,
  })
}

export function useUpdateIncome() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: SaveIncome }) => api.updateIncome(id, data),
    onSuccess: invalidate,
  })
}

export function useDeleteTransaction() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (id: number) => api.deleteTransaction(id), onSuccess: invalidate })
}

export function useOpeningBalance() {
  return useQuery({ queryKey: queryKeys.openingBalance, queryFn: () => api.getOpeningBalance() })
}

export function useSetOpeningBalance() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: (b: SaveOpeningBalance) => api.setOpeningBalance(b),
    onSuccess: invalidate,
  })
}

export function useClearOpeningBalance() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: () => api.clearOpeningBalance(), onSuccess: invalidate })
}

export function useCreateRecurring() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (r: SaveRecurring) => api.createRecurring(r), onSuccess: invalidate })
}

export function useUpdateRecurring() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: SaveRecurring }) => api.updateRecurring(id, data),
    onSuccess: invalidate,
  })
}

export function useDeleteRecurring() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (id: number) => api.deleteRecurring(id), onSuccess: invalidate })
}

export function useConfirmCharge() {
  const invalidate = useInvalidateEverything()
  return useMutation({
    mutationFn: (transactionId: number) => api.confirmCharge(transactionId),
    onSuccess: invalidate,
  })
}

export function useTaxProfile() {
  return useQuery({ queryKey: queryKeys.taxProfile, queryFn: () => api.getTaxProfile() })
}

export function useTaxDefaults() {
  return useQuery({ queryKey: queryKeys.taxDefaults, queryFn: () => api.getTaxDefaults(), staleTime: Infinity })
}

export function useSaveTaxProfile() {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: (p: SaveTaxProfile) => api.saveTaxProfile(p), onSuccess: invalidate })
}

/// Live income preview. Debounced so typing "24600" is one request, not five. Only meaningful
/// in the base currency — the tax engine works in PLN.
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

function useSavingsMutation<T>(fn: (v: T) => Promise<unknown>) {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: fn, onSuccess: invalidate })
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

/// Envelopes as a thing in their own right, sharing the movements' invalidation: the list of
/// jars lives inside `savings`, and an empty jar holds nothing back from the norm — but the
/// next one might.
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

export function useDebts() {
  return useQuery({ queryKey: queryKeys.debts, queryFn: api.getDebts })
}

/// Every debt write re-reads everything, like every other money decision: a repayment out of
/// spendable money moves the daily norm, one out of a jar moves that jar, and money coming
/// back moves the budget itself.
function useDebtMutation<T>(fn: (v: T) => Promise<unknown>) {
  const invalidate = useInvalidateEverything()
  return useMutation({ mutationFn: fn, onSuccess: invalidate })
}

export function useCreateDebt() {
  return useDebtMutation((d: SaveDebt) => api.createDebt(d))
}

export function useUpdateDebt() {
  return useDebtMutation(({ id, data }: { id: number; data: SaveDebt }) => api.updateDebt(id, data))
}

export function useDeleteDebt() {
  return useDebtMutation((id: number) => api.deleteDebt(id))
}

export function useSetDebtClosed() {
  return useDebtMutation(({ id, closed }: { id: number; closed: boolean }) =>
    api.setDebtClosed(id, closed))
}

export function useAddDebtPayment() {
  return useDebtMutation(({ id, data }: { id: number; data: SaveDebtPayment }) =>
    api.addDebtPayment(id, data))
}

export function useDeleteDebtPayment() {
  return useDebtMutation((paymentId: number) => api.deleteDebtPayment(paymentId))
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
