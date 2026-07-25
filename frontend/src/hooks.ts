import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type { SaveCategory, SaveIncome, SaveRecurring, SaveTaxProfile, SaveTransaction } from './types'

export const queryKeys = {
  categories: ['categories'] as const,
  transactions: ['transactions'] as const,
  budget: ['budget'] as const,
  summary: ['summary'] as const,
  recurring: ['recurring'] as const,
  taxProfile: ['taxProfile'] as const,
  taxDefaults: ['taxDefaults'] as const,
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

export function useCalculateTakeHome() {
  return useMutation({
    mutationFn: ({ amount, includesVat }: { amount: number; includesVat: boolean }) =>
      api.calculateTakeHome(amount, includesVat),
  })
}
