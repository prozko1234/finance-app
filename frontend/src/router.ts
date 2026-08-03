import { useCallback, useEffect, useState } from 'react'
import type { View } from './components/Nav'

/// Екран у адресі, а не тільки в пам'яті вкладки.
///
/// До цього застосунок тримав відкритий екран у `useState<View>`, і з цього виходило три
/// щоденні незручності: «назад» на телефоні виходило із застосунку замість повернення на
/// попередній екран, оновлення сторінки викидало на головну, і на екран не можна було дати
/// посилання самому собі.
///
/// History API, не хеш: сервер уже віддає `index.html` на будь-який шлях
/// (`MapFallbackToFile` у `Program.cs`), тож адреси лишаються чистими.
const PATHS: Record<View, string> = {
  home: '/',
  add: '/add',
  balance: '/balance',
  savings: '/savings',
  allocation: '/allocation',
  recurring: '/recurring',
  stats: '/stats',
  categories: '/categories',
  tax: '/tax',
  settings: '/settings',
  account: '/account',
  import: '/import',
  dev: '/dev',
}

const BY_PATH = new Map<string, View>(
  Object.entries(PATHS).map(([view, path]) => [path, view as View]),
)

/// Екран і, необовʼязково, що саме на ньому відкрито: `/savings/3` — банка 3. Один рівень
/// углиб, бо саме там «назад» найпотрібніше: банку відкривають із її ж списку.
export interface Route {
  view: View
  param: string | null
}

export function pathOf(view: View, param?: string | null): string {
  const base = PATHS[view] ?? PATHS.home
  if (!param) return base
  return base === '/' ? `/${param}` : `${base}/${param}`
}

export function routeOf(pathname: string): Route {
  const [first = '', second = ''] = pathname.replace(/^\/+|\/+$/g, '').split('/')
  const view = BY_PATH.get(`/${first}`)
  // Невідома адреса — головна: помилятись у бік екрана, який завжди має що показати.
  if (!view) return { view: 'home', param: null }
  return { view, param: second || null }
}

export interface Router extends Route {
  go: (view: View, param?: string | null) => void
}

export function useRouter(): Router {
  const [route, setRoute] = useState<Route>(() => routeOf(window.location.pathname))

  // Кнопка «назад» браузера й телефона — це той самий popstate; без цього рядка адреса
  // мінялась би, а екран лишався б на місці.
  useEffect(() => {
    const onPop = () => setRoute(routeOf(window.location.pathname))
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  const go = useCallback((view: View, param: string | null = null) => {
    const path = pathOf(view, param)
    if (path === window.location.pathname) return

    window.history.pushState(null, '', path)
    setRoute({ view, param })
    // Новий екран починається згори. Назад цього не робить свідомо: там людина повертається
    // туди, де вже була, і місце в списку — частина того, куди вона повертається.
    window.scrollTo(0, 0)
  }, [])

  return { ...route, go }
}
