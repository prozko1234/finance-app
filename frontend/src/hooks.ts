import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type { SaveTransaction } from './types'

export const queryKeys = {
  categories: ['categories'] as const,
  transactions: ['transactions'] as const,
  budget: ['budget'] as const,
  summary: ['summary'] as const,
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

/// After any write, the derived data (transactions, summary, budget) is stale — refetch it.
function useInvalidate() {
  const qc = useQueryClient()
  return () => {
    qc.invalidateQueries({ queryKey: queryKeys.transactions })
    qc.invalidateQueries({ queryKey: queryKeys.summary })
    qc.invalidateQueries({ queryKey: queryKeys.budget })
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
