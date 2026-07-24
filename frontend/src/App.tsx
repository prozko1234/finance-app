import { useCallback, useEffect, useState } from 'react'
import { api } from './api'
import type { Budget, Category, SafeToSpend, SaveTransaction, Transaction } from './types'
import { Home } from './components/Home'
import { AddTransaction } from './components/AddTransaction'
import { Settings } from './components/Settings'

type View = 'home' | 'add' | 'settings'

function App() {
  const [view, setView] = useState<View>('home')
  const [categories, setCategories] = useState<Category[]>([])
  const [summary, setSummary] = useState<SafeToSpend | null>(null)
  const [transactions, setTransactions] = useState<Transaction[]>([])
  const [budget, setBudget] = useState<Budget | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      const [s, t, b] = await Promise.all([
        api.getSafeToSpend(),
        api.getTransactions(),
        api.getBudget(),
      ])
      setSummary(s)
      setTransactions(t)
      setBudget(b)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Немає зв\'язку з бекендом')
    }
  }, [])

  useEffect(() => {
    api.getCategories().then(setCategories).catch(() => {})
    reload()
  }, [reload])

  async function handleSave(tx: SaveTransaction) {
    await api.createTransaction(tx)
    await reload()
    setView('home')
  }

  async function handleDelete(id: number) {
    await api.deleteTransaction(id)
    await reload()
  }

  async function handleBudget(amount: number) {
    await api.setBudget(amount)
    await reload()
  }

  return (
    <div className="min-h-screen">
      <div className="mx-auto max-w-md px-4 py-6 pb-28">
        <header className="flex items-center justify-between mb-6">
          <h1 className="text-xl font-bold">finance</h1>
          {view === 'home' && (
            <button onClick={() => setView('settings')} className="text-neutral-400 text-xl" aria-label="Налаштування">
              ⚙
            </button>
          )}
        </header>

        {error && (
          <div className="mb-4 rounded-xl bg-red-50 dark:bg-red-950 text-red-700 dark:text-red-300 px-4 py-3 text-sm">
            {error}
          </div>
        )}

        {view === 'home' && (
          <Home
            summary={summary}
            transactions={transactions}
            onDelete={handleDelete}
            onGoSettings={() => setView('settings')}
          />
        )}
        {view === 'add' && (
          <AddTransaction categories={categories} onSave={handleSave} onCancel={() => setView('home')} />
        )}
        {view === 'settings' && (
          <Settings budget={budget} onSave={handleBudget} onBack={() => setView('home')} />
        )}
      </div>

      {view === 'home' && (
        <button
          onClick={() => setView('add')}
          className="fixed bottom-6 left-1/2 -translate-x-1/2 h-14 w-14 rounded-full bg-emerald-600 text-white text-3xl shadow-lg flex items-center justify-center"
          aria-label="Додати транзакцію"
        >
          +
        </button>
      )}
    </div>
  )
}

export default App
