import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import type { Plugin } from 'vite'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const rootDir = fileURLToPath(new URL('.', import.meta.url))

function registerRoute(): Plugin {
  return {
    name: 'stocklab-register-route',
    configureServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/register') request.url = '/register/'
function loginRoute(): Plugin {
  return {
    name: 'stocklab-login-route',
    configureServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/login') request.url = '/login/'
        next()
      })
    },
    configurePreviewServer(server) {
      server.middlewares.use((request, _response, next) => {
        if (request.url === '/register') request.url = '/register/'
        if (request.url === '/login') request.url = '/login/'
        next()
      })
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      input: {
        app: 'index.html',
        market: 'market.html',
  plugins: [react(), registerRoute()],
  plugins: [react(), loginRoute()],
  build: {
    rollupOptions: {
      input: {
        main: resolve(rootDir, 'index.html'),
        register: resolve(rootDir, 'register/index.html'),
        login: resolve(rootDir, 'login/index.html'),
      },
    },
  },
})
