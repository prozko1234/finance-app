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

  /// Друге видалення підтверджує перше: черга з кількох «Повернути» означала б, що треба
  /// пам'ятати, який саме рядок повернеться.
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

  /// Закрив застосунок — видалення все одно має відбутись: людина вже побачила його
  /// зробленим, і рядок, що повернувся сам, читався б як загублена дія.
  it('commits a pending delete when the screen goes away', () => {
    const commit = vi.fn()
    const { result, unmount } = renderHook(() => useDeferredDelete(5000))

    act(() => result.current.request(3, 'Рух видалено', commit))
    unmount()

    expect(commit).toHaveBeenCalledTimes(1)
  })
})
