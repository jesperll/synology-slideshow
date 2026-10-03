import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { writeFileSync } from 'fs'
import { resolve } from 'path'

// https://vitejs.dev/config/
// Generated once per build and reused for both the version-check poll and the
// app.css cache-busting query param below, so a stale cached stylesheet can't
// survive a deploy the way the previously-unversioned /app.css link allowed.
const buildHash = Date.now().toString(36) + Math.random().toString(36).substring(2)

export default defineConfig({
  plugins: [
    react(),
    {
      name: 'generate-version',
      transformIndexHtml(html) {
        return html.replace(
          '<link rel="stylesheet" href="/app.css" />',
          `<link rel="stylesheet" href="/app.css?v=${buildHash}" />`
        )
      },
      writeBundle() {
        // Write version file to dist folder
        const versionFile = resolve(__dirname, 'dist', 'version.json')
        writeFileSync(versionFile, JSON.stringify({ hash: buildHash }))
      }
    }
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5154',
        changeOrigin: true,
        secure: false
      },
      '/hub': {
        target: 'http://localhost:5154',
        changeOrigin: true,
        ws: true
      }
    }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test-setup.ts']
  }
})
