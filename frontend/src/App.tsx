import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { setOnUnauthorized } from './api'
import { Login } from './components/Login'
import type { Recurring as RecurringType, SaveCategory, SaveIncome, SaveTransaction, Transaction } from './types'
import {
  useBudget, useCategories, useCreateRecurring, useCreateTransaction, useDeleteRecurring,
  useCreateCategory, useCreateIncome, useUpdateTransaction, useDeleteCategory, useDeleteTransaction, useRecurring, useUpdateCategory, useSafeToSpend, useSetBudget, useTransactions,
  useUpdateRecurring, useTaxProfile, useTaxDefaults, useSaveTaxProfile,
  useAllocations, useSaveAllocation, useSettings, useSetDisplayCurrency, useSetPeriodStartDay,
  useSavings, useSaveSavingsPlan, useAddSavingsEntry, useUpdateSavingsEntry, useDeleteSavingsEntry,
  useStats, useAuthStatus, useLogin, useLogout, queryKeys,
  useChangePassword, useChangeEmail, useSignOutEverywhere,
  useOpeningBalance, useSetOpeningBalance,
} from './hooks'
import { Onboarding } from './components/Onboarding'
import { Home } from './components/Home'
import { AddTransaction } from './components/AddTransaction'
import { Settings } from './components/Settings'
import { Account } from './components/Account'
import { Recurring } from './components/Recurring'
import { TaxProfile } from './components/TaxProfile'
import { Categories } from './components/Categories'
import { Savings } from './components/Savings'
import { Allocation } from './components/Allocation'
import { Stats, MONTHS_BACK } from './components/Stats'
import { DevTools } from './components/DevTools'
import { Nav } from './components/Nav'
import type { View } from './components/Nav'

function App() {
  const [view, setView] = useState<View>('home')
  const [editingTx, setEditingTx] = useState<Transaction | null>(null)
  // Set when a quick-category tap opens the form; cleared as soon as the form closes.
  const [presetCategoryId, setPresetCategoryId] = useState<number | null>(null)
  // null = whichever month the server considers current; set once the user taps a bar.
  const [statsMonth, setStatsMonth] = useState<string | null>(null)
  // Survives a reload: "я сам розберусь" must not be asked again on every refresh.
  const [skippedOnboarding, setSkipped] = useState(() => localStorage.getItem('onboarded') === '1')
  const skipOnboarding = () => { localStorage.setItem('onboarded', '1'); setSkipped(true) }

  const qc = useQueryClient()
  const auth = useAuthStatus()
  const login = useLogin()
  const logout = useLogout()
  const changePassword = useChangePassword()
  const changeEmail = useChangeEmail()
  const signOutEverywhere = useSignOutEverywhere()

  // A cookie can expire while the app is open. Any 401 sends us back to asking.
  useEffect(() => setOnUnauthorized(() => { qc.invalidateQueries({ queryKey: queryKeys.auth }) }), [qc])

  const categories = useCategories()
  const summary = useSafeToSpend()
  const transactions = useTransactions()
  const budget = useBudget()
  const openingBalance = useOpeningBalance()
  const setOpeningBalance = useSetOpeningBalance()
  const recurring = useRecurring()
  const savings = useSavings()
  const allocations = useAllocations()
  const saveAllocation = useSaveAllocation()
  const saveSavingsPlan = useSaveSavingsPlan()
  const addSavingsEntry = useAddSavingsEntry()
  const updateSavingsEntry = useUpdateSavingsEntry()
  const deleteSavingsEntry = useDeleteSavingsEntry()
  const stats = useStats(MONTHS_BACK, statsMonth, view === 'stats')
  const settings = useSettings()
  const setDisplayCurrency = useSetDisplayCurrency()
  const setPeriodStartDay = useSetPeriodStartDay()
  const taxProfile = useTaxProfile()
  const taxDefaults = useTaxDefaults()
  const saveTaxProfile = useSaveTaxProfile()

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

  // Nothing is rendered before we know whether a password is wanted — a flash of the
  // dashboard would show the balance to someone who has not passed the door yet.
  if (auth.isPending) return null
  if (auth.data?.required && !auth.data.authenticated)
    return <Login onSubmit={(c) => login.mutateAsync(c).then(() => {})} />

  // An empty app is indistinguishable from a broken one: the old home said "бюджет ще не
  // заданий" and left the rest of the screen blank. Derived from the data rather than from
  // a stored flag, so it cannot get stuck on for someone who already has a budget.
  const untouched =
    !skippedOnboarding &&
    budget.isSuccess && !budget.data.set &&
    openingBalance.isSuccess && !openingBalance.data.isSet &&
    transactions.isSuccess && transactions.data.length === 0

  if (untouched) {
    return (
      <div className="min-h-screen">
        <div className="mx-auto max-w-md px-4 py-10">
          <Onboarding
            currency={settings.data?.displayCurrency ?? 'PLN'}
            onSkip={skipOnboarding}
            onFinish={async ({ budget: b, balance }) => {
              if (b !== null) await setBudget.mutateAsync(b)
              if (balance !== null) await setOpeningBalance.mutateAsync({ amount: balance })
              skipOnboarding()
            }}
          />
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen">
      <div className="mx-auto max-w-4xl px-4 py-6 pb-28 md:flex md:gap-8">
        <Nav
          current={view}
          onGo={setView}
          showDev={import.meta.env.DEV}
          onLogout={auth.data?.required ? () => logout.mutate() : undefined}
        />

        <main className="flex-1 md:max-w-md">
        <header className="mb-6 md:hidden">
          <h1 className="text-xl font-bold">finance</h1>
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
            onGoSavings={() => setView('savings')}
            onGoAllocation={() => setView('allocation')}
            onQuickCategory={(categoryId) => { setPresetCategoryId(categoryId); setView('add') }}
            onEdit={startEdit}
          />
        )}
        {view === 'add' && (
          <AddTransaction
            categories={categories.data ?? []}
            onSave={handleSave}
            onSaveIncome={async (i: SaveIncome) => { await createIncome.mutateAsync(i); setView('home') }}
            onSaveRecurring={async (r) => { await createRecurring.mutateAsync(r); setView('home') }}
            onCreateCategory={(c: SaveCategory) => createCategory.mutateAsync(c)}
            onCancel={() => { setEditingTx(null); setPresetCategoryId(null); setView('home') }}
            editing={editingTx}
            presetCategoryId={presetCategoryId}
          />
        )}
        {view === 'settings' && (
          <Settings
            budget={budget.data ?? null}
            settings={settings.data ?? null}
            /// The month budget as reported — already in the currency the user reads.
            /// monthTaxes.takeHome would be the PLN figure and would arrive mislabelled.
            incomeBudget={summary.data?.monthTaxes ? summary.data.periodBudget ?? null : null}
            onPickCurrency={(c) => setDisplayCurrency.mutateAsync(c).then(() => {})}
            onPickPeriodStartDay={(d) => setPeriodStartDay.mutateAsync(d).then(() => {})}
            onSave={(amount) => setBudget.mutateAsync(amount).then(() => {})}
            onBack={() => setView('home')}
          />
        )}
        {view === 'account' && (
          <Account
            email={auth.data?.email ?? null}
            onChangePassword={(current, next) =>
              changePassword.mutateAsync({ current, next }).then(() => {})}
            onChangeEmail={(password, email) =>
              changeEmail.mutateAsync({ password, email }).then(() => {})}
            // Ends this session too, so the login screen is where the user lands: no
            // navigation needed, the auth query going false does it.
            onSignOutEverywhere={() => signOutEverywhere.mutateAsync().then(() => {})}
            onLogout={() => logout.mutate()}
            onBack={() => setView('home')}
          />
        )}
        {view === 'dev' && <DevTools onBack={() => setView('home')} />}
        {view === 'savings' && (
          <Savings
            data={savings.data ?? null}
            onSavePlan={(p) => saveSavingsPlan.mutateAsync(p).then(() => {})}
            onAddEntry={(e) => addSavingsEntry.mutateAsync(e).then(() => {})}
            onUpdateEntry={(id, e) => updateSavingsEntry.mutateAsync({ id, data: e }).then(() => {})}
            onDeleteEntry={(id) => deleteSavingsEntry.mutateAsync(id).then(() => {})}
            onBack={() => setView('home')}
          />
        )}
        {view === 'allocation' && (
          <Allocation
            data={allocations.data ?? null}
            budget={summary.data?.periodBudget ?? null}
            currency={summary.data?.currency ?? 'PLN'}
            onSave={(a) => saveAllocation.mutateAsync(a).then(() => {})}
            onBack={() => setView('home')}
          />
        )}
        {view === 'stats' && (
          <Stats
            data={stats.data ?? null}
            selected={statsMonth}
            onSelectMonth={setStatsMonth}
            onBack={() => setView('home')}
          />
        )}
        {view === 'recurring' && (
          <Recurring
            categories={categories.data ?? []}
            items={recurring.data ?? []}
            onCreate={(r) => createRecurring.mutateAsync(r).then(() => {})}
            onToggle={toggleRecurring}
            onDelete={(id) => deleteRecurring.mutateAsync(id).then(() => {})}
            onBack={() => setView('home')}
          />
        )}
        {view === 'categories' && (
          <Categories
            categories={categories.data ?? []}
            onCreate={(c) => createCategory.mutateAsync(c)}
            onUpdate={(id, data) => updateCategory.mutateAsync({ id, data }).then(() => {})}
            onDelete={(id) => deleteCategory.mutateAsync(id).then(() => {})}
            onBack={() => setView('home')}
          />
        )}
        {view === 'tax' && (
          <TaxProfile
            profile={taxProfile.data ?? null}
            defaults={taxDefaults.data ?? null}
            onSave={(p) => saveTaxProfile.mutateAsync(p).then(() => {})}
            onBack={() => setView('home')}
          />
        )}
        </main>
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
