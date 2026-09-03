import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const rootDir = fileURLToPath(new URL('.', import.meta.url))

function portfolioRoute(): Plugin {
  return {
    name: 'stocklab-portfolio-route',
    configureServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/portfolio') request.url = '/portfolio/'
        next()
      })
    },
    configurePreviewServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/portfolio') request.url = '/portfolio/'
        next()
      })
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), portfolioRoute()],
  build: {
    rollupOptions: {
      input: {
        main: resolve(rootDir, 'index.html'),
        portfolio: resolve(rootDir, 'portfolio/index.html'),
      },
    },
  },
})
