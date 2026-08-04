import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import './index.css'
import App from './App.tsx'
import { markRunningVersionHealthy } from './liveUpdate'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      // A locked door does not open on the second knock: retrying a 401 only multiplies
      // the requests every screen fires while the login form is up.
      retry: (count, error) => !String(error.message).includes('401') && count < 1,
    },
  },
})

// Tell the shell this build really did start. Silence means "broken", and the update rolls
// back to the previous working version — which is exactly why this call sits after the render
// rather than before it.
void markRunningVersionHealthy()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
)
