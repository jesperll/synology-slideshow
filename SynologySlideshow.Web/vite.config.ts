import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { writeFileSync } from 'fs'
import { resolve } from 'path'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react(),
    {
      name: 'generate-version',
      writeBundle() {
        // Generate a unique build hash at build time
        const buildHash = Date.now().toString(36) + Math.random().toString(36).substring(2)
        
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
      }
    }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true
  }
})
