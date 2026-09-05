import react from '@vitejs/plugin-react'
import { defineConfig, type Plugin } from 'vite'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { documentUrl, pages } from './src/navigation/routes.ts'

const rootDir = fileURLToPath(new URL('.', import.meta.url))

function pageRoutes(): Plugin {
  const rewrite = (request: { url?: string }, _response: unknown, next: () => void) => {
    if (request.url) request.url = documentUrl(request.url)
    next()
  }
  return {
    name: 'stocklab-page-routes',
    configureServer(server) { server.middlewares.use(rewrite) },
    configurePreviewServer(server) { server.middlewares.use(rewrite) },
  }
}

export default defineConfig({
  plugins: [react(), pageRoutes()],
  build: {
    rollupOptions: {
      input: {
        main: resolve(rootDir, 'index.html'),
        ...Object.fromEntries(Object.entries(pages).map(([id, page]) => [id, resolve(rootDir, page.entry)])),
      },
    },
  },
})
