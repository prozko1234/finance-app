import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { TaxActualsBlock } from './TaxActualsBlock'
import { api } from '../api'

const actuals = {
  month: '2026-08-01',
  zusSocial: null as number | null,
  health: null as number | null,
  pit: null as number | null,
  computedZusSocial: 1600,
  computedHealth: 460,
  computedPit: 1128,
  currency: 'PLN',
}

function wrap(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>)
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(api, 'getTaxActuals').mockResolvedValue({ ...actuals })
  vi.spyOn(api, 'saveTaxActuals').mockResolvedValue({ ...actuals })
})

/// The engine is a model. The figure that actually gets paid comes from a person with the full
/// picture, and until now the only way to reconcile the two was to stop believing the app.
describe('податки за місяць', () => {
  it('says whose figures are in force', async () => {
    wrap(<TaxActualsBlock />)
    expect(await screen.findByText(/рахує застосунок/)).toBeInTheDocument()
  })

  it('says so when they were typed by hand', async () => {
    vi.spyOn(api, 'getTaxActuals').mockResolvedValue({ ...actuals, zusSocial: 1000 })
    wrap(<TaxActualsBlock />)
    expect(await screen.findByText(/вписані руками/)).toBeInTheDocument()
  })

  /// Monthly, not per invoice — Polish contributions are, and a field sitting under an invoice
  /// reads as belonging to it.
  it('says the figures are for the month, not the invoice', async () => {
    wrap(<TaxActualsBlock />)
    await userEvent.click(await screen.findByText(/Податки за місяць/))
    expect(screen.getByText(/за весь місяць, а не за цю фактуру/)).toBeInTheDocument()
  })

  it('sends what was typed and leaves the rest to the engine', async () => {
    const save = vi.spyOn(api, 'saveTaxActuals').mockResolvedValue({ ...actuals, zusSocial: 1000 })
    wrap(<TaxActualsBlock />)

    await userEvent.click(await screen.findByText(/Податки за місяць/))
    await userEvent.type(screen.getByLabelText('ZUS społeczne'), '1000')
    await userEvent.click(screen.getByText(/Зберегти податки місяця/))

    await waitFor(() => expect(save).toHaveBeenCalledWith({
      month: '2026-08-01', zusSocial: 1000, health: null, pit: null,
    }))
  })

  /// Clearing a box is a real answer — "рахуй сам" — and must not be saved as a zero.
  it('sends an emptied field as null, never as zero', async () => {
    vi.spyOn(api, 'getTaxActuals').mockResolvedValue({ ...actuals, zusSocial: 1000 })
    const save = vi.spyOn(api, 'saveTaxActuals').mockResolvedValue({ ...actuals })
    wrap(<TaxActualsBlock />)

    await userEvent.click(await screen.findByText(/Податки за місяць/))
    await userEvent.clear(screen.getByLabelText('ZUS społeczne'))
    await userEvent.click(screen.getByText(/Зберегти податки місяця/))

    await waitFor(() => expect(save).toHaveBeenCalledWith({
      month: '2026-08-01', zusSocial: null, health: null, pit: null,
    }))
  })
})
