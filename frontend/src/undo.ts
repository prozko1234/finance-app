import { useCallback, useEffect, useRef, useState } from 'react'

/// Видалення, яке ще можна забрати назад. Рядок зникає одразу — це чесний фідбек, — але
/// запит на сервер іде лише через кілька секунд, і «Повернути» його просто скасовує.
///
/// До цього в застосунку жили три різні патерни: транзакція і рух у банці видалялись одним
/// тапом без питань і без вороття, а підписка — через «познач і підтверди». Тобто найдорожча
/// втрата (запис про гроші) була найдешевшою в один тап. Підтвердження на кожен тап — не
/// вихід: діалог, який бачиш щодня, перестаєш читати, а зайве рішення тут — це те, від чого
/// застосунок і має звільняти.
///
/// Скасувати можна лише одне останнє видалення: друге підтверджує перше. Черга з кількох
/// «Повернути» означала б, що треба пам'ятати, який саме рядок повернеться.
const DELAY_MS = 5000

export interface DeferredDelete {
  /// Id, які список має приховати, поки видалення ще можна скасувати.
  hidden: number[]
  /// Текст для панелі «Повернути», або null — коли нічого не висить.
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

  // Застосунок закривають — видалення все одно має відбутись: людина вже побачила його
  // зробленим, і рядок, що повернувся сам, читався б як загублена дія.
  useEffect(() => () => {
    if (!job.current) return
    clearTimeout(job.current.timer)
    job.current.commit()
  }, [])

  return { hidden: pending ? [pending.id] : [], label: pending?.label ?? null, request, undo }
}
