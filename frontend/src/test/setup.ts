import '@testing-library/jest-dom'

// This jsdom build ships without Storage, and Node's own localStorage is off unless
// --localstorage-file is passed. Anything reading localStorage would silently no-op
// in tests, so give it a real in-memory implementation instead.
if (typeof globalThis.localStorage === 'undefined') {
  const store = new Map<string, string>()
  const localStorageStub: Storage = {
    get length() { return store.size },
    key: (i: number) => [...store.keys()][i] ?? null,
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, String(v)),
    removeItem: (k: string) => void store.delete(k),
    clear: () => store.clear(),
  }
  Object.defineProperty(globalThis, 'localStorage', { value: localStorageStub, writable: true })
}
