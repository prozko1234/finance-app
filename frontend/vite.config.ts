import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // Proxy to the backend — the frontend calls a relative /api, no CORS needed in dev.
    proxy: {
      '/api': 'http://localhost:5099',
    },
  },
})
