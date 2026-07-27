import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type {
  SaveCategory, SaveIncome, SaveRecurring, SaveSavingsEntry, SaveSavingsPlan, SaveTaxProfile, SaveTransaction,
} from './types'

export const queryKeys = {
  categories: ['categories'] as const,
  transactions: ['transactions'] as const,
  budget: ['budget'] as const,
  summary: ['summary'] as const,
  recurring: ['recurring'] as const,
  taxProfile: ['taxProfile'] as const,
  taxDefaults: ['taxDefaults'] as const,
  savings: ['savings'] as const,
  incomePreview: ['incomePreview'] as const,
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

export function useTransactions() {
  return useQuery({ queryKey: queryKeys.transactions, queryFn: () => api.getTransactions() })
}

export function useBudget() {
  return useQuery({ queryKey: queryKeys.budget, queryFn: () => api.getBudget() })
}

export function useSafeToSpend() {
  return useQuery({ queryKey: queryKeys.summary, queryFn: () => api.getSafeToSpend() })
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
    qc.invalidateQueries({ queryKey: queryKeys.budget })
    qc.invalidateQueries({ queryKey: queryKeys.recurring })
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

export function useDeleteTransaction() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => api.deleteTransaction(id), onSuccess: invalidate })
}

export function useSetBudget() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (amount: number) => api.setBudget(amount), onSuccess: invalidate })
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

export function useDeleteSavingsEntry() {
  return useSavingsMutation((id: number) => api.deleteSavingsEntry(id))
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
