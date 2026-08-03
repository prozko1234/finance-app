import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { setOnUnauthorized } from './api'
import { useDeferredDelete } from './undo'
import { UndoBar } from './components/Screen'
import { Login } from './components/Login'
import type { Recurring as RecurringType, SaveCategory, SaveIncome, SaveTransaction, Transaction } from './types'
import {
  useCategories, useCreateRecurring, useCreateTransaction, useDeleteRecurring,
  useCreateCategory, useCreateIncome, useUpdateTransaction, useUpdateIncome, useDeleteCategory, useDeleteTransaction, useRecurring, useUpdateCategory, useSafeToSpend, useTransactions,
  useUpdateRecurring, useTaxProfile, useTaxDefaults, useSaveTaxProfile,
  useAllocations, useSaveAllocation, useSettings, useSetDisplayCurrency, useSetPeriodStartDay,
  useSavings, useSaveSavingsPlan, useAddSavingsEntry, useUpdateSavingsEntry, useDeleteSavingsEntry,
  useCreateEnvelope, useUpdateEnvelope, useDeleteEnvelope, useSetEnvelopeTarget, useTransferBetweenEnvelopes,
  useStats, useAuthStatus, useLogin, useLogout, queryKeys,
  useChangePassword, useChangeEmail, useSignOutEverywhere, useDevices, useRevokeDevice,
  useImportPreview, useCommitImport,
  useOpeningBalance, useSetOpeningBalance, useClearOpeningBalance,
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
import { Allocation } from './components/Allocation'
import { Stats, MONTHS_BACK } from './components/Stats'
import { DevTools } from './components/DevTools'
import { Nav } from './components/Nav'
import { useRouter } from './router'

const RECENT_PAGE = 20

function App() {
  // Екран живе в адресі: «назад» на телефоні, refresh на місці, посилання на екран
  // ([[router.ts]]).
  const { view, param, go } = useRouter()
  const [editingTx, setEditingTx] = useState<Transaction | null>(null)
  // Set when a quick-category tap opens the form; cleared as soon as the form closes.
  const [presetCategoryId, setPresetCategoryId] = useState<number | null>(null)
  // Головна просить дохід — форма має відкритись одразу на вкладці «Дохід», інакше
  // кнопка веде на витрату і питання лишається без відповіді.
  const [incomeFirst, setIncomeFirst] = useState(false)
  // null = whichever month the server considers current; set once the user taps a bar.
  const [statsMonth, setStatsMonth] = useState<string | null>(null)
  // Те саме, що й із онбордингом: питання про день зарплати, на яке вже відповіли —
  // хоч «так і є» — більше не ставиться.
  const [paydayAsked, setPaydayAsked] = useState(() => localStorage.getItem('paydayAsked') === '1')
  const dismissPayday = () => { localStorage.setItem('paydayAsked', '1'); setPaydayAsked(true) }

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
  const devices = useDevices()
  const revokeDevice = useRevokeDevice()
  const importPreview = useImportPreview()
  const commitImport = useCommitImport()

  // A cookie can expire while the app is open. Any 401 sends us back to asking.
  useEffect(() => setOnUnauthorized(() => { qc.invalidateQueries({ queryKey: queryKeys.auth }) }), [qc])

  const categories = useCategories()
  const summary = useSafeToSpend()
  // Скільки останніх записів показуємо. Росте по 20 на «Показати ще»: список на весь екран
  // щодня не потрібен, а «а де та витрата минулого тижня» трапляється.
  const [recentTake, setRecentTake] = useState(RECENT_PAGE)
  const transactions = useTransactions(recentTake)
  const openingBalance = useOpeningBalance()
  const setOpeningBalance = useSetOpeningBalance()
  const clearOpeningBalance = useClearOpeningBalance()
  const recurring = useRecurring()
  const savings = useSavings()
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

  // Видалення з відкладеним підтвердженням — по одному на список, бо id з різних таблиць
  // збігаються, і спільний «сховати id 3» приховав би заодно чужий рядок.
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

  function startEdit(t: Transaction) {
    setEditingTx(t)
    go('add')
  }

  /// Що саме сталося при видаленні. Списання, яке зробила підписка, і звичайна витрата
  /// зникають однаково, але означають різне: підписка спише знову наступного разу, і про це
  /// краще сказати одразу, ніж лишити людину гадати, чи вона щойно її скасувала.
  ///
  /// Сказано тостом, а не питанням: підтверджувати кожне видалення — це зупиняти людину там,
  /// де вона й так може все повернути ([[undo-instead-of-confirmations]]).
  function deletedLabel(id: number): string {
    const tx = (transactions.data ?? []).find((t) => t.id === id)
    if (tx?.source !== 'Recurring') return 'Запис видалено'

    return `Це списання прибрано · ${tx.note || tx.categoryName} спише як завжди`
  }

  function toggleRecurring(r: RecurringType) {
    updateRecurring.mutate({
      id: r.id,
      // kind і amountIncludesVat їдуть теж: без них пауза на регулярному доході робила з нього
      // витрату (сервер добирав вид за замовчуванням), і місяць тихо втрачав дохід і отримував
      // списання. Сервер тепер теж тримає вид, якщо його не назвали, — але казати правду в
      // запиті дешевше, ніж покладатись на це.
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
  if (auth.data?.required && !auth.data.authenticated)
    return <Login onSubmit={(c) => login.mutateAsync(c).then(() => {})} />

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
              // Дохід першим: із нього рахується бюджет, і залишок має лягати вже поверх
              // нього, а не навпаки.
              if (income !== null) {
                await createIncome.mutateAsync({
                  amount: income, amountIncludesVat: false, currency: settings.data?.displayCurrency ?? 'PLN',
                })
              }
              if (balance !== null) await setOpeningBalance.mutateAsync({ amount: balance })
              skipOnboarding()
              // Податки — окремий екран, а не ще три кроки в онбордингу: там ставки,
              // режим і ZUS, і це рішення на пів хвилини, а не на першу мінуту.
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
            // Сервер віддав рівно стільки, скільки просили — отже, найімовірніше, є ще.
            canLoadMore={(transactions.data ?? []).length >= recentTake}
            onLoadMore={() => setRecentTake((n) => n + RECENT_PAGE)}
            onDelete={(id) => txUndo.request(id, deletedLabel(id), () => deleteTx.mutate(id))}
            onAddIncome={() => { setIncomeFirst(true); go('add') }}
            onGoSavings={() => go('savings')}
            onGoAllocation={() => go('allocation')}
            onGoBalance={() => go('balance')}
            onQuickCategory={(categoryId) => { setPresetCategoryId(categoryId); go('add') }}
            onEdit={startEdit}
            // Онбординг показується лише порожньому застосунку, тож той, хто вже мав дані,
            // ніколи не бачив питання про день зарплати — і живе з періодом із 1 числа.
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
            // Тільки ті, де є що витрачати: банка з нулем як джерело — це вибір,
            // який нічого не дає.
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
            currency={settings.data?.displayCurrency ?? 'PLN'}
            onSet={(b) => setOpeningBalance.mutateAsync(b).then(() => {})}
            onClear={() => clearOpeningBalance.mutateAsync().then(() => {})}
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
            // Не через «Повернути»: сервер відмовляє, якщо в банці ще є гроші, і відкладений
            // запит доносив би цю відмову вже після того, як екран закрився. Прибрати можна
            // лише порожню банку, а повертається вона тим самим ім'ям.
            onArchiveEnvelope={(id) => deleteEnvelope.mutateAsync(id).then(() => {})}
            onSetTarget={(id, t) => setEnvelopeTarget.mutateAsync({ id, data: t }).then(() => {})}
            onTransfer={(t) => transferBetweenEnvelopes.mutateAsync(t).then(() => {})}
            // Відкрита банка — теж адреса (`/savings/3`), інакше «назад» із неї виходило б
            // зі списку банок замість повернення до нього.
            openId={param ? Number(param) : null}
            onOpen={(id) => go('savings', id === null ? null : String(id))}
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
            // Розклад податків цього періоду переїхав сюди з головної — там він був
            // розкривачкою, тут він поруч зі ставками, які його й порахували.
            month={summary.data?.monthTaxes ?? null}
            onSave={(p) => saveTaxProfile.mutateAsync(p).then(() => {})}
            onBack={() => go('home')}
          />
        )}
        </main>
      </div>

      {undo && <UndoBar label={undo.label!} onUndo={undo.undo} />}

      {/* Десктоп: на телефоні цю роль грає центр нижньої панелі, і дві кнопки «+» на одному
          екрані були б двома різними відповідями на те саме питання. */}
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
