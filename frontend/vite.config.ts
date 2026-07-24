import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // Проксі на бекенд — фронт звертається до відносного /api, CORS у розробці не потрібен.
    proxy: {
      '/api': 'http://localhost:5099',
    },
  },
})
