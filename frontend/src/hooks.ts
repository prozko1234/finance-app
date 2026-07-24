import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type { SaveRecurring, SaveTransaction } from './types'

export const queryKeys = {
  categories: ['categories'] as const,
  transactions: ['transactions'] as const,
  budget: ['budget'] as const,
  summary: ['summary'] as const,
  recurring: ['recurring'] as const,
}

export function useCategories() {
  return useQuery({ queryKey: queryKeys.categories, queryFn: () => api.getCategories() })
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
