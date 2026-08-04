import { useCallback, useEffect, useState } from 'react'
import type { View } from './components/Nav'

/// The screen lives in the address, not only in the tab's memory.
///
/// The app used to keep the open screen in `useState<View>`, which cost three annoyances a
/// day: back on a phone left the app instead of returning to the previous screen, a refresh
/// dropped you on the home screen, and there was no way to link yourself to a screen.
///
/// History API rather than a hash: the server already serves `index.html` for any path
/// (`MapFallbackToFile` in `Program.cs`), so the addresses stay clean.
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

/// The screen and, optionally, what is open on it: `/savings/3` is jar 3. One level deep,
/// because that is where back matters most — a jar is opened from its own list.
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
  // An unknown address falls back to home: err towards the screen that always has something.
  if (!view) return { view: 'home', param: null }
  return { view, param: second || null }
}

export interface Router extends Route {
  go: (view: View, param?: string | null) => void
}

export function useRouter(): Router {
  const [route, setRoute] = useState<Route>(() => routeOf(window.location.pathname))

  // The browser's back button and the phone's are the same popstate; without this the address
  // would change while the screen stayed put.
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
    // A new screen starts at the top. Back deliberately does not: there the user is returning
    // to somewhere they have been, and the place in the list is part of where they return to.
    window.scrollTo(0, 0)
  }, [])

  return { ...route, go }
}
