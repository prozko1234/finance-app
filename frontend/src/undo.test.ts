import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useDeferredDelete } from './undo'

describe('useDeferredDelete', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('hides the row at once but sends nothing until the window passes', () => {
    const commit = vi.fn()
    const { result } = renderHook(() => useDeferredDelete(5000))

    act(() => result.current.request(7, 'Запис видалено', commit))

    expect(result.current.hidden).toEqual([7])
    expect(result.current.label).toBe('Запис видалено')
    expect(commit).not.toHaveBeenCalled()

    act(() => { vi.advanceTimersByTime(5000) })

    expect(commit).toHaveBeenCalledTimes(1)
    expect(result.current.hidden).toEqual([])
    expect(result.current.label).toBeNull()
  })

  it('never sends the delete when it was taken back', () => {
    const commit = vi.fn()
    const { result } = renderHook(() => useDeferredDelete(5000))

    act(() => result.current.request(7, 'Запис видалено', commit))
    act(() => result.current.undo())
    act(() => { vi.advanceTimersByTime(10_000) })

    expect(commit).not.toHaveBeenCalled()
    expect(result.current.hidden).toEqual([])
  })

  /// A second delete confirms the first: a queue of undos would mean remembering which row is
  /// about to come back.
  it('commits the previous delete when another one starts', () => {
    const first = vi.fn()
    const second = vi.fn()
    const { result } = renderHook(() => useDeferredDelete(5000))

    act(() => result.current.request(1, 'Перший', first))
    act(() => result.current.request(2, 'Другий', second))

    expect(first).toHaveBeenCalledTimes(1)
    expect(second).not.toHaveBeenCalled()
    expect(result.current.hidden).toEqual([2])
  })

  /// The app closed, and the delete must still happen: the user has already seen it done, and a
  /// row that came back by itself would read as a lost action.
  it('commits a pending delete when the screen goes away', () => {
    const commit = vi.fn()
    const { result, unmount } = renderHook(() => useDeferredDelete(5000))

    act(() => result.current.request(3, 'Рух видалено', commit))
    unmount()

    expect(commit).toHaveBeenCalledTimes(1)
  })
})
