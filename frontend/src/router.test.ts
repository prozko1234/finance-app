import { describe, it, expect } from 'vitest'
import { pathOf, routeOf } from './router'

describe('router', () => {
  it('maps a screen to an address and back', () => {
    expect(pathOf('savings')).toBe('/savings')
    expect(routeOf('/savings')).toEqual({ view: 'savings', param: null })
    expect(pathOf('home')).toBe('/')
    expect(routeOf('/')).toEqual({ view: 'home', param: null })
  })

  /// An opened jar is an address too — otherwise back would leave the jar list entirely.
  it('keeps the open jar in the address', () => {
    expect(pathOf('savings', '3')).toBe('/savings/3')
    expect(routeOf('/savings/3')).toEqual({ view: 'savings', param: '3' })
  })

  /// Err towards the screen that always has something to show.
  it('falls back to the home screen for an address it does not know', () => {
    expect(routeOf('/nonsense')).toEqual({ view: 'home', param: null })
    expect(routeOf('')).toEqual({ view: 'home', param: null })
  })

  it('reads an address with a trailing slash the same way', () => {
    expect(routeOf('/stats/')).toEqual({ view: 'stats', param: null })
  })
})
