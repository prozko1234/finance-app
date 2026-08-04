import { useCallback, useEffect, useRef, useState } from 'react'

/// A delete that can still be taken back. The row disappears at once — honest feedback — but
/// the request only leaves for the server a few seconds later, and undo simply cancels it.
///
/// The app used to have three different patterns: a transaction and a jar movement went on one
/// tap, no questions and no way back, while a subscription took mark-then-confirm. The most
/// expensive loss — a record about money — was the cheapest single tap. Confirming every tap is
/// not the answer either: a dialog seen daily stops being read, and an extra decision is
/// exactly what this app exists to remove.
///
/// Only the last delete can be undone; a second one confirms the first. A queue of undos would
/// mean remembering which row is about to come back.
const DELAY_MS = 5000

export interface DeferredDelete {
  /// The ids the list must hide while the delete can still be cancelled.
  hidden: number[]
  /// The label for the undo bar, or null when nothing is pending.
  label: string | null
  request: (id: number, label: string, commit: () => void) => void
  undo: () => void
}

export function useDeferredDelete(delayMs = DELAY_MS): DeferredDelete {
  const [pending, setPending] = useState<{ id: number; label: string } | null>(null)
  const job = useRef<{ commit: () => void; timer: ReturnType<typeof setTimeout> } | null>(null)

  const commitNow = useCallback(() => {
    const current = job.current
    if (!current) return
    clearTimeout(current.timer)
    job.current = null
    current.commit()
    setPending(null)
  }, [])

  const request = useCallback((id: number, label: string, commit: () => void) => {
    commitNow()
    const timer = setTimeout(() => {
      job.current = null
      commit()
      setPending(null)
    }, delayMs)
    job.current = { commit, timer }
    setPending({ id, label })
  }, [commitNow, delayMs])

  const undo = useCallback(() => {
    if (job.current) clearTimeout(job.current.timer)
    job.current = null
    setPending(null)
  }, [])

  // The app is closing, and the delete must still happen: the user has already seen it done,
  // and a row that came back by itself would read as a lost action.
  useEffect(() => () => {
    if (!job.current) return
    clearTimeout(job.current.timer)
    job.current.commit()
  }, [])

  return { hidden: pending ? [pending.id] : [], label: pending?.label ?? null, request, undo }
}
