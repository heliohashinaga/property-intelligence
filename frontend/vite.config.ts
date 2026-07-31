import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  // VITE_API_BASE_URL is injected at build time by Cloudflare Pages environment variables.
  // Set it to the deployed API origin (e.g. https://api.propertyintelligence.com.br).
  // Leaving it unset defaults to '' (same-origin), which works when the API and frontend
  // are served behind a single Cloudflare tunnel.
  define: {
    'import.meta.env.VITE_API_BASE_URL': JSON.stringify(process.env.VITE_API_BASE_URL ?? ''),
  },
  build: {
    outDir: 'dist',
  },
})
