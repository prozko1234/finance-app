/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: 'autoUpdate',
      // Generate all icons (favicon, apple-touch, maskable) from one SVG source.
      pwaAssets: { image: 'public/logo.svg' },
      workbox: {
        // Charge reminders, pulled into the generated worker. Kept as an imported script
        // rather than switching the whole build to `injectManifest`: two event listeners are
        // not worth putting the app's offline behaviour at risk.
        importScripts: ['push-sw.js'],
      },
      manifest: {
        name: 'finance',
        short_name: 'finance',
        description: 'Знай одну цифру: скільки безпечно витратити сьогодні',
        lang: 'uk',
        theme_color: '#059669',
        background_color: '#0a0a0a',
        display: 'standalone',
        start_url: '/',
      },
    }),
  ],
  server: {
    // Proxy to the backend — the frontend calls a relative /api, no CORS needed in dev.
    proxy: {
      '/api': 'http://localhost:5099',
    },
  },
  // Same proxy for the production preview (used to test the installable PWA / phone).
  preview: {
    proxy: {
      '/api': 'http://localhost:5099',
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/test/setup.ts',
  },
})
