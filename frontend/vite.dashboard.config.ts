import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'dist-dashboard',
    emptyOutDir: true,
    rollupOptions: {
      input: fileURLToPath(new URL('./dashboard.html', import.meta.url)),
    },
  },
})
