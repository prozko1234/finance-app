import { useState } from 'react'
import type { Recurring as RecurringType, SaveCategory, SaveIncome, SaveTransaction, Transaction } from './types'
import {
  useBudget, useCategories, useCreateRecurring, useCreateTransaction, useDeleteRecurring,
  useCreateCategory, useCreateIncome, useUpdateTransaction, useDeleteCategory, useDeleteTransaction, useRecurring, useUpdateCategory, useSafeToSpend, useSetBudget, useTransactions,
  useUpdateRecurring, useTaxProfile, useTaxDefaults, useSaveTaxProfile, useCalculateTakeHome,
} from './hooks'
import { Home } from './components/Home'
import { AddTransaction } from './components/AddTransaction'
import { Settings } from './components/Settings'
import { Recurring } from './components/Recurring'
import { Tax } from './components/Tax'
import { Categories } from './components/Categories'
import type { QuickAction } from './quickRepeat'

type View = 'home' | 'add' | 'settings' | 'recurring' | 'tax' | 'categories'

function App() {
  const [view, setView] = useState<View>('home')
  const [editingTx, setEditingTx] = useState<Transaction | null>(null)

  const categories = useCategories()
  const summary = useSafeToSpend()
  const transactions = useTransactions()
  const budget = useBudget()
  const recurring = useRecurring()
  const taxProfile = useTaxProfile()
  const taxDefaults = useTaxDefaults()
  const saveTaxProfile = useSaveTaxProfile()
  const takeHome = useCalculateTakeHome()

  const createTx = useCreateTransaction()
  const createIncome = useCreateIncome()
  const updateTx = useUpdateTransaction()
  const createCategory = useCreateCategory()
  const updateCategory = useUpdateCategory()
  const deleteCategory = useDeleteCategory()
  const deleteTx = useDeleteTransaction()
  const setBudget = useSetBudget()
  const createRecurring = useCreateRecurring()
  const updateRecurring = useUpdateRecurring()
  const deleteRecurring = useDeleteRecurring()

  const loadError = summary.error ?? transactions.error ?? budget.error

  async function handleSave(tx: SaveTransaction) {
    if (editingTx) {
      await updateTx.mutateAsync({ id: editingTx.id, data: tx })
      setEditingTx(null)
    } else {
      await createTx.mutateAsync(tx)
    }
    setView('home')
  }

  function startEdit(t: Transaction) {
    setEditingTx(t)
    setView('add')
  }

  function toggleRecurring(r: RecurringType) {
    updateRecurring.mutate({
      id: r.id,
      data: {
        amount: r.amountOriginal, currency: r.currencyOriginal, categoryId: r.categoryId,
        dayOfMonth: r.dayOfMonth, note: r.note ?? null, active: !r.active,
      },
    })
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

        {loadError && (
          <div className="mb-4 rounded-xl bg-red-50 dark:bg-red-950 text-red-700 dark:text-red-300 px-4 py-3 text-sm">
            Немає зв'язку з бекендом. Запусти сервер і онови сторінку.
          </div>
        )}

        {view === 'home' && (
          <Home
            summary={summary.data ?? null}
            transactions={transactions.data ?? []}
            onDelete={(id) => deleteTx.mutate(id)}
            onGoSettings={() => setView('settings')}
            onQuickRepeat={(a: QuickAction) => createTx.mutate({
              amount: a.amount,
              currency: a.currency,
              categoryId: a.categoryId,
              priority: 'Should',
              frequency: 'OneOff',
              note: null,
            })}
            onEdit={startEdit}
          />
        )}
        {view === 'add' && (
          <AddTransaction
            categories={categories.data ?? []}
            onSave={handleSave}
            onSaveIncome={async (i: SaveIncome) => { await createIncome.mutateAsync(i); setView('home') }}
            onCreateCategory={(c: SaveCategory) => createCategory.mutateAsync(c)}
            onCancel={() => { setEditingTx(null); setView('home') }}
            editing={editingTx}
          />
        )}
        {view === 'settings' && (
          <Settings
            budget={budget.data ?? null}
            onSave={(amount) => setBudget.mutateAsync(amount).then(() => {})}
            onBack={() => setView('home')}
            onGoRecurring={() => setView('recurring')}
            onGoTax={() => setView('tax')}
            onGoCategories={() => setView('categories')}
          />
        )}
        {view === 'recurring' && (
          <Recurring
            categories={categories.data ?? []}
            items={recurring.data ?? []}
            onCreate={(r) => createRecurring.mutateAsync(r).then(() => {})}
            onToggle={toggleRecurring}
            onDelete={(id) => deleteRecurring.mutate(id)}
            onBack={() => setView('settings')}
          />
        )}
        {view === 'categories' && (
          <Categories
            categories={categories.data ?? []}
            onCreate={(c) => createCategory.mutateAsync(c)}
            onUpdate={(id, data) => updateCategory.mutateAsync({ id, data }).then(() => {})}
            onDelete={(id) => deleteCategory.mutateAsync(id).then(() => {})}
            onBack={() => setView('settings')}
          />
        )}
        {view === 'tax' && (
          <Tax
            profile={taxProfile.data ?? null}
            defaults={taxDefaults.data ?? null}
            result={takeHome.data ?? null}
            onSaveProfile={(p) => saveTaxProfile.mutateAsync(p).then(() => {})}
            onCalculate={(amount, includesVat) => takeHome.mutate({ amount, includesVat })}
            onBack={() => setView('settings')}
          />
        )}
      </div>

      {view === 'home' && (
        <button
          onClick={() => { setEditingTx(null); setView('add') }}
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
