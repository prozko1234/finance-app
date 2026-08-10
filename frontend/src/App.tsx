import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { setOnUnauthorized } from './api'
import { useDeferredDelete } from './undo'
import { UndoBar } from './components/Screen'
import { Login, inviteCodeFromUrl } from './components/Login'
import type { Horizon, Recurring as RecurringType, SaveCategory, SaveIncome, SaveTransaction, Transaction } from './types'
import { readHorizon, writeLastUsed } from './lastUsed'
import {
  useCategories, useIncomeCategories, useConfirmCharge, useCreateRecurring, useCreateTransaction, useDeleteRecurring, useMonthlyNeed,
  useCreateCategory, useCreateIncome, useUpdateTransaction, useUpdateIncome, useDeleteCategory, useDeleteTransaction, useFrequentCategories, useRecurring, useUpdateCategory, useSafeToSpend, useTransactions,
  useUpdateRecurring, useTaxProfile, useTaxDefaults, useSaveTaxProfile,
  useAllocations, useSaveAllocation, useSettings, useSetDisplayCurrency, useSetPeriodStartDay,
  useSavings, useSaveSavingsPlan, useAddSavingsEntry, useUpdateSavingsEntry, useDeleteSavingsEntry,
  useDebts, useCreateDebt, useDeleteDebt, useSetDebtClosed, useAddDebtPayment,
  useCreateEnvelope, useUpdateEnvelope, useDeleteEnvelope, useSetEnvelopeTarget, useTransferBetweenEnvelopes,
  useStats, useAuthStatus, useLogin, useLogout, useRegister, queryKeys,
  useChangePassword, useChangeEmail, useSignOutEverywhere, useDevices, useRevokeDevice,
  useInvites, useCreateInvite, useRevokeInvite,
  useImportPreview, useCommitImport,
  useOpeningBalance, useSetOpeningBalance, useClearOpeningBalance, useDecideCarryover,
} from './hooks'
import { Onboarding } from './components/Onboarding'
import { Home } from './components/Home'
import { Balance } from './components/Balance'
import { AddTransaction } from './components/AddTransaction'
import { Settings } from './components/Settings'
import { Account } from './components/Account'
import { Recurring } from './components/Recurring'
import { TaxProfile } from './components/TaxProfile'
import { Categories } from './components/Categories'
import { Import } from './components/Import'
import { Savings } from './components/Savings'
import { Debts } from './components/Debts'
import { Allocation } from './components/Allocation'
import { Stats, MONTHS_BACK } from './components/Stats'
import { DevTools } from './components/DevTools'
import { Nav } from './components/Nav'
import { useRouter } from './router'

const RECENT_PAGE = 20

function App() {
  // The screen lives in the address: the phone's back button, refresh staying put, a link to
  // a screen ([[router.ts]]).
  const { view, param, go } = useRouter()
  const [editingTx, setEditingTx] = useState<Transaction | null>(null)
  // Set when a quick-category tap opens the form; cleared as soon as the form closes.
  const [presetCategoryId, setPresetCategoryId] = useState<number | null>(null)
  // The home screen asks for income, so the form must open on the income tab — otherwise the
  // button leads to an expense and the question goes unanswered.
  const [incomeFirst, setIncomeFirst] = useState(false)
  // null = whichever month the server considers current; set once the user taps a bar.
  const [statsMonth, setStatsMonth] = useState<string | null>(null)
  // Same as onboarding: a payday question that has been answered — even with "yes, the 1st" —
  // is not asked again.
  const [paydayAsked, setPaydayAsked] = useState(() => localStorage.getItem('paydayAsked') === '1')
  const dismissPayday = () => { localStorage.setItem('paydayAsked', '1'); setPaydayAsked(true) }

  // Survives a reload: "я сам розберусь" must not be asked again on every refresh.
  const [skippedOnboarding, setSkipped] = useState(() => localStorage.getItem('onboarded') === '1')
  const skipOnboarding = () => { localStorage.setItem('onboarded', '1'); setSkipped(true) }

  const qc = useQueryClient()
  const auth = useAuthStatus()
  const login = useLogin()
  const register = useRegister()
  const logout = useLogout()
  const changePassword = useChangePassword()
  const changeEmail = useChangeEmail()
  const signOutEverywhere = useSignOutEverywhere()
  const devices = useDevices()
  const revokeDevice = useRevokeDevice()
  // Only fetched for the owner: for anyone else the endpoint refuses, and asking would be a
  // guaranteed error in the console on every visit to the account screen.
  const invites = useInvites(auth.data?.isOwner === true && view === 'account')
  const createInvite = useCreateInvite()
  const revokeInvite = useRevokeInvite()
  const importPreview = useImportPreview()
  const commitImport = useCommitImport()

  // A cookie can expire while the app is open. Any 401 sends us back to asking.
  useEffect(() => setOnUnauthorized(() => { qc.invalidateQueries({ queryKey: queryKeys.auth }) }), [qc])

  const categories = useCategories()
  const incomeCategories = useIncomeCategories()
  const summary = useSafeToSpend()
  // How many recent rows we show. Grows by 20 on "Показати ще": a full screen of history is
  // not wanted every day, but "where was that expense last week" does happen.
  const [recentTake, setRecentTake] = useState(RECENT_PAGE)
  const transactions = useTransactions(recentTake)
  const openingBalance = useOpeningBalance()
  const setOpeningBalance = useSetOpeningBalance()
  const clearOpeningBalance = useClearOpeningBalance()
  const decideCarryover = useDecideCarryover()
  const confirmCharge = useConfirmCharge()
  const monthlyNeed = useMonthlyNeed()
  // Read once at mount and kept in state: localStorage is the store, this is the value.
  const [horizon, setHorizon] = useState<Horizon>(readHorizon)
  const recurring = useRecurring()
  const frequentCategories = useFrequentCategories()
  const savings = useSavings()
  const debts = useDebts()
  const createDebt = useCreateDebt()
  const deleteDebt = useDeleteDebt()
  const setDebtClosed = useSetDebtClosed()
  const addDebtPayment = useAddDebtPayment()
  const allocations = useAllocations()
  const saveAllocation = useSaveAllocation()
  const saveSavingsPlan = useSaveSavingsPlan()
  const addSavingsEntry = useAddSavingsEntry()
  const updateSavingsEntry = useUpdateSavingsEntry()
  const deleteSavingsEntry = useDeleteSavingsEntry()
  const createEnvelope = useCreateEnvelope()
  const updateEnvelope = useUpdateEnvelope()
  const deleteEnvelope = useDeleteEnvelope()
  const setEnvelopeTarget = useSetEnvelopeTarget()
  const transferBetweenEnvelopes = useTransferBetweenEnvelopes()
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
  const updateIncome = useUpdateIncome()
  const createCategory = useCreateCategory()
  const updateCategory = useUpdateCategory()
  const deleteCategory = useDeleteCategory()
  const deleteTx = useDeleteTransaction()
  const createRecurring = useCreateRecurring()
  const updateRecurring = useUpdateRecurring()
  const deleteRecurring = useDeleteRecurring()

  // Deferred deletes, one per list: ids from different tables collide, and a shared "hide
  // id 3" would hide somebody else's row along with it.
  const txUndo = useDeferredDelete()
  const entryUndo = useDeferredDelete()
  const recurringUndo = useDeferredDelete()
  const undo = [txUndo, entryUndo, recurringUndo].find((u) => u.label !== null) ?? null

  const loadError = summary.error ?? transactions.error

  async function handleSave(tx: SaveTransaction) {
    if (editingTx) {
      await updateTx.mutateAsync({ id: editingTx.id, data: tx })
      setEditingTx(null)
    } else {
      await createTx.mutateAsync(tx)
    }
    go('home')
  }

  /// The reconcile screen's two explaining answers. A gap is written down as what it actually
  /// was — an expense or an income — rather than being flattened into the account's figure, so
  /// it lands in the history, counts in the statistics, and can be undone like anything else.
  ///
  /// The category is the one the starting set keeps for "нічого з переліченого": the gap has
  /// no category by construction, and asking for one on a screen about a difference would put
  /// a decision in the way of the correction.
  async function recordGap(kind: 'expense' | 'income', amount: number) {
    const note = 'Звірка з банком'
    // The gap is only ever offered in the currency the summary is read in — the screen refuses
    // to subtract a hryvnia count from a złoty total — so that is the currency it is saved in.
    const currency = summary.data?.currency ?? 'PLN'
    if (kind === 'income') {
      await createIncome.mutateAsync({ amount, amountIncludesVat: false, currency, note })
      return
    }

    const rows = categories.data ?? []
    const other = rows.find((c) => c.isSystem) ?? rows[0]
    if (!other) throw new Error('Немає жодної категорії, щоб записати витрату.')

    await createTx.mutateAsync({
      amount, currency, categoryId: other.id, frequency: 'OneOff', note,
    })
  }

  function startEdit(t: Transaction) {
    setEditingTx(t)
    go('add')
  }

  /// What a delete actually did. A charge a subscription made and an ordinary expense vanish
  /// the same way but mean different things: the subscription will charge again next time,
  /// and saying so beats leaving the user to wonder whether they just cancelled it.
  ///
  /// Said as a toast, not asked as a question: confirming every delete stops people where
  /// they can already undo everything ([[undo-instead-of-confirmations]]).
  function deletedLabel(id: number): string {
    const tx = (transactions.data ?? []).find((t) => t.id === id)
    if (tx?.source !== 'Recurring') return 'Запис видалено'

    return `Це списання прибрано · ${tx.note || tx.categoryName} спише як завжди`
  }

  function toggleRecurring(r: RecurringType) {
    updateRecurring.mutate({
      id: r.id,
      // kind and amountIncludesVat travel too: without them, pausing a recurring INCOME turned
      // it into an expense (the server filled in the default kind), and the month quietly lost
      // the income and gained a charge. The server now keeps the kind when it is not named —
      // but telling the truth in the request is cheaper than relying on that.
      data: {
        amount: r.amountOriginal, currency: r.currencyOriginal, categoryId: r.categoryId,
        startsOn: r.startsOn, unit: r.unit, interval: r.interval,
        note: r.note ?? null, active: !r.active,
        kind: r.kind, amountIncludesVat: r.amountIncludesVat,
      },
    })
  }

  // Nothing is rendered before we know whether a password is wanted — a flash of the
  // dashboard would show the balance to someone who has not passed the door yet.
  if (auth.isPending) return null
  if (auth.data?.required && !auth.data.authenticated) {
    return (
      <Login
        invite={inviteCodeFromUrl()}
        onSubmit={(c) => login.mutateAsync(c).then(() => {})}
        onRegister={(r) => register.mutateAsync(r).then(() => {
          // The code is spent, and a reload that replayed it would only fail. Dropping it
          // also keeps a working credential out of the address bar and the history.
          window.history.replaceState({}, '', window.location.pathname)
        })}
      />
    )
  }

  // An empty app is indistinguishable from a broken one, so the first run walks through
  // setup instead of showing an empty screen. Derived from the data rather than from a
  // stored flag, so it cannot get stuck on for someone who already has money in the app.
  const untouched =
    !skippedOnboarding &&
    openingBalance.isSuccess && !openingBalance.data.isSet &&
    transactions.isSuccess && transactions.data.length === 0

  if (untouched) {
    return (
      <div className="min-h-screen">
        <div className="mx-auto max-w-md px-4 py-10">
          <Onboarding
            currency={settings.data?.displayCurrency ?? 'PLN'}
            onSkip={skipOnboarding}
            onFinish={async ({ periodStartDay, income, balance, setUpTaxes }) => {
              await setPeriodStartDay.mutateAsync(periodStartDay)
              // Income first: the budget is computed from it, and the counted balance has to
              // land on top of that rather than the other way round.
              if (income !== null) {
                await createIncome.mutateAsync({
                  amount: income, amountIncludesVat: false, currency: settings.data?.displayCurrency ?? 'PLN',
                })
              }
              if (balance !== null) await setOpeningBalance.mutateAsync({ amount: balance })
              skipOnboarding()
              // Taxes get their own screen instead of three more onboarding steps: rates,
              // regime and ZUS are half a minute of decisions, not first-minute ones.
              if (setUpTaxes) go('tax')
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
          onGo={(v) => go(v)}
          onAdd={() => { setEditingTx(null); go('add') }}
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
            transactions={(transactions.data ?? []).filter((t) => !txUndo.hidden.includes(t.id))}
            // The server returned exactly as many rows as asked for, so there are probably more.
            canLoadMore={(transactions.data ?? []).length >= recentTake}
            onLoadMore={() => setRecentTake((n) => n + RECENT_PAGE)}
            onDelete={(id) => txUndo.request(id, deletedLabel(id), () => deleteTx.mutate(id))}
            onAddIncome={() => { setIncomeFirst(true); go('add') }}
            onGoSavings={() => go('savings')}
            onGoAllocation={() => go('allocation')}
            onGoBalance={() => go('balance')}
            onDecideCarryover={(d) => decideCarryover.mutate(d)}
            onConfirmCharge={(id) => confirmCharge.mutate(id)}
            horizon={horizon}
            onHorizon={(h) => { setHorizon(h); writeLastUsed({ horizon: h }) }}
            frequent={frequentCategories.data ?? []}
            onQuickCategory={(categoryId) => { setPresetCategoryId(categoryId); go('add') }}
            onEdit={startEdit}
            // Onboarding only shows on an empty app, so anyone who already had data never saw
            // the payday question — and is still living with a period that starts on the 1st.
            paydayNudge={
              !paydayAsked && settings.data?.periodStartDay === 1 && (transactions.data ?? []).length > 0
                ? { onGo: () => { dismissPayday(); go('settings') }, onDismiss: dismissPayday }
                : null
            }
          />
        )}
        {view === 'add' && (
          <AddTransaction
            categories={categories.data ?? []}
            incomeCategories={incomeCategories.data ?? []}
            // Only jars with something in them: an empty jar as a source is a choice that
            // does nothing.
            envelopes={(summary.data?.envelopes ?? []).filter((e) => e.balance > 0)}
            onSave={handleSave}
            onSaveIncome={async (i: SaveIncome) => { await createIncome.mutateAsync(i); go('home') }}
            onUpdateIncome={async (id, i) => {
              await updateIncome.mutateAsync({ id, data: i })
              setEditingTx(null)
              go('home')
            }}
            onSaveRecurring={async (r) => { await createRecurring.mutateAsync(r); go('home') }}
            onCreateCategory={(c: SaveCategory) => createCategory.mutateAsync(c)}
            onCancel={() => { setEditingTx(null); setPresetCategoryId(null); setIncomeFirst(false); go('home') }}
            editing={editingTx}
            presetCategoryId={presetCategoryId}
            initialKind={incomeFirst ? 'income' : 'expense'}
          />
        )}
        {view === 'balance' && (
          <Balance
            data={openingBalance.data ?? null}
            summary={summary.data ?? null}
            need={monthlyNeed.data ?? null}
            currency={settings.data?.displayCurrency ?? 'PLN'}
            onSet={(b) => setOpeningBalance.mutateAsync(b).then(() => {})}
            onClear={() => clearOpeningBalance.mutateAsync().then(() => {})}
            onRecordGap={recordGap}
            onBack={() => go('home')}
          />
        )}
        {view === 'settings' && (
          <Settings
            settings={settings.data ?? null}
            onPickCurrency={(c) => setDisplayCurrency.mutateAsync(c).then(() => {})}
            onPickPeriodStartDay={(d) => setPeriodStartDay.mutateAsync(d).then(() => {})}
            onBack={() => go('home')}
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
            devices={devices.data ?? []}
            onRevokeDevice={(id) => revokeDevice.mutateAsync(id).then(() => {})}
            isOwner={auth.data?.isOwner ?? false}
            invites={invites.data ?? []}
            onCreateInvite={(note) => createInvite.mutateAsync(note)}
            onRevokeInvite={(id) => revokeInvite.mutateAsync(id).then(() => {})}
            onBack={() => go('home')}
          />
        )}
        {view === 'import' && (
          <Import
            categories={categories.data ?? []}
            onPreview={(file) => importPreview.mutateAsync(file)}
            onCommit={(rows) => commitImport.mutateAsync(rows)}
            onDone={() => go('home')}
            onBack={() => go('home')}
          />
        )}
        {view === 'dev' && <DevTools onBack={() => go('home')} />}
        {view === 'savings' && (
          <Savings
            data={savings.data
              ? { ...savings.data, recent: savings.data.recent.filter((e) => !entryUndo.hidden.includes(e.id)) }
              : null}
            onSavePlan={(p) => saveSavingsPlan.mutateAsync(p).then(() => {})}
            onAddEntry={(e) => addSavingsEntry.mutateAsync(e).then(() => {})}
            onUpdateEntry={(id, e) => updateSavingsEntry.mutateAsync({ id, data: e }).then(() => {})}
            onDeleteEntry={async (id) =>
              entryUndo.request(id, 'Рух видалено', () => deleteSavingsEntry.mutate(id))}
            onCreateEnvelope={(e) => createEnvelope.mutateAsync(e).then(() => {})}
            onUpdateEnvelope={(id, e) => updateEnvelope.mutateAsync({ id, data: e }).then(() => {})}
            // Not through the undo bar: the server refuses while there is money in the jar,
            // and a deferred request would deliver that refusal after the screen had closed.
            // Only an empty jar can be put away, and it comes back under the same name.
            onArchiveEnvelope={(id) => deleteEnvelope.mutateAsync(id).then(() => {})}
            onSetTarget={(id, t) => setEnvelopeTarget.mutateAsync({ id, data: t }).then(() => {})}
            onTransfer={(t) => transferBetweenEnvelopes.mutateAsync(t).then(() => {})}
            // An opened jar is an address too (`/savings/3`); otherwise back would leave the
            // jar list entirely instead of returning to it.
            openId={param ? Number(param) : null}
            onOpen={(id) => go('savings', id === null ? null : String(id))}
            onBack={() => go('home')}
          />
        )}
        {view === 'debts' && (
          <Debts
            data={debts.data ?? null}
            // The jars come from the savings query, which is already loaded for the home
            // screen: a debt paid out of a jar has to name one that really exists.
            envelopes={savings.data?.envelopes ?? []}
            onCreate={(d) => createDebt.mutateAsync(d).then(() => {})}
            onDelete={(id) => deleteDebt.mutateAsync(id).then(() => {})}
            onSetClosed={(id, closed) => setDebtClosed.mutateAsync({ id, closed }).then(() => {})}
            onPay={(id, p) => addDebtPayment.mutateAsync({ id, data: p }).then(() => {})}
            onBack={() => go('home')}
          />
        )}
        {view === 'allocation' && (
          <Allocation
            data={allocations.data ?? null}
            budget={summary.data?.periodBudget ?? null}
            currency={summary.data?.currency ?? 'PLN'}
            onSave={(a) => saveAllocation.mutateAsync(a).then(() => {})}
            onBack={() => go('home')}
          />
        )}
        {view === 'stats' && (
          <Stats
            data={stats.data ?? null}
            recurring={recurring.data ?? []}
            selected={statsMonth}
            onSelectMonth={setStatsMonth}
            onBack={() => go('home')}
          />
        )}
        {view === 'recurring' && (
          <Recurring
            categories={categories.data ?? []}
            items={(recurring.data ?? []).filter((r) => !recurringUndo.hidden.includes(r.id))}
            onCreate={(r) => createRecurring.mutateAsync(r).then(() => {})}
            onUpdate={(id, r) => updateRecurring.mutateAsync({ id, data: r }).then(() => {})}
            onToggle={toggleRecurring}
            onDelete={async (id) =>
              recurringUndo.request(id, 'Підписку видалено', () => deleteRecurring.mutate(id))}
            onBack={() => go('home')}
          />
        )}
        {view === 'categories' && (
          <Categories
            categories={categories.data ?? []}
            onCreate={(c) => createCategory.mutateAsync(c)}
            onUpdate={(id, data) => updateCategory.mutateAsync({ id, data }).then(() => {})}
            onDelete={(id) => deleteCategory.mutateAsync(id).then(() => {})}
            onBack={() => go('home')}
          />
        )}
        {view === 'tax' && (
          <TaxProfile
            profile={taxProfile.data ?? null}
            defaults={taxDefaults.data ?? null}
            // This period's tax split moved here from the home screen: there it was a
            // disclosure, here it sits beside the rates that produced it.
            month={summary.data?.monthTaxes ?? null}
            onSave={(p) => saveTaxProfile.mutateAsync(p).then(() => {})}
            onBack={() => go('home')}
          />
        )}
        </main>
      </div>

      {undo && <UndoBar label={undo.label!} onUndo={undo.undo} />}

      {/* Desktop only: on a phone the centre of the bottom bar plays this role, and two "+"
          buttons on one screen would be two different answers to the same question. */}
      {view === 'home' && (
        <button
          onClick={() => { setEditingTx(null); go('add') }}
          // Fixed elements sit against the viewport, not the body, so the safe-area padding
          // on <body> does not reach them — on a home-indicator iPhone this button would end
          // up half under the bar that swipes the app away.
          style={{ bottom: 'calc(1.5rem + env(safe-area-inset-bottom))' }}
          className="hidden md:flex fixed left-1/2 -translate-x-1/2 h-14 w-14 rounded-full bg-emerald-600 text-white text-3xl shadow-lg items-center justify-center"
          aria-label="Додати транзакцію"
        >
          +
        </button>
      )}
    </div>
  )
}

export default App
