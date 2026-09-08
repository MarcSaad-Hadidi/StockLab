import assert from 'node:assert/strict'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { test } from 'node:test'
import { build, createServer, preview } from 'vite'
import { pages } from '../src/navigation/routes.ts'

const root = fileURLToPath(new URL('..', import.meta.url))

// Real HTTP requests catch Vite fallbacks and middleware ordering that helper
// tests cannot detect. Each configuration must serve every linked document.
for (const config of ['vite.config.ts', 'vite.dashboard.config.ts']) {
  test(`${config} serves the same routes in dev and built preview`, async (t) => {
    const outDir = await mkdtemp(join(tmpdir(), 'stocklab-routing-'))
    t.after(() => rm(outDir, { recursive: true, force: true }))
    const nodeEnv = process.env.NODE_ENV
    t.after(() => {
      if (nodeEnv === undefined) delete process.env.NODE_ENV
      else process.env.NODE_ENV = nodeEnv
    })
    const configFile = join(root, config)
    await build({ root, configFile, logLevel: 'silent', build: { outDir, emptyOutDir: true } })

    for (const mode of ['dev', 'preview'] as const) {
      await t.test(mode, async (t) => {
        // Vite build sets NODE_ENV=production. Do not reuse production React
        // prebundles in dev or alter the cache of a developer's running server.
        process.env.NODE_ENV = mode === 'dev' ? 'development' : 'production'
        const options = { root, configFile, cacheDir: join(outDir, '.vite-test'), logLevel: 'silent' as const }
        const server = mode === 'dev'
          ? await createServer({ ...options, server: { host: '127.0.0.1', port: 0 } })
          : await preview({ ...options, build: { outDir }, preview: { host: '127.0.0.1', port: 0 } })
        t.after(() => server.close())
        if ('listen' in server) await server.listen()
        const base = server.resolvedUrls!.local[0]

        await t.test('clean, trailing slash and legacy URLs keep queries and select the right document', async () => {
          for (const page of Object.values(pages)) {
            const html = await readFile(join(mode === 'dev' ? root : outDir, page.entry), 'utf8')
            const scripts = [...html.matchAll(/<script\b[^>]*\bsrc="([^"]+)"/g)].map(match => match[1])
            assert.ok(scripts.length > 0, page.entry)
            for (const path of [page.path, `${page.path}/`, `/${page.entry}`]) {
              const url = new URL(`${path}?symbol=aapl&return=%2Fportfolio%3Fx%3D1`, base).href
              const response = await fetch(url)
              assert.equal(response.status, 200, url)
              assert.equal(response.url, url, 'rewrites must not redirect the visible URL')
              const body = await response.text()
              for (const script of scripts) assert.ok(body.includes(`src="${script}"`), `${url}: missing ${script}`)
            }
          }
          const unknown = await fetch(new URL('/unknown/nested/?symbol=aapl', base))
          const notFound = await fetch(new URL('/not-found?symbol=aapl', base))
          assert.equal(await unknown.text(), await notFound.text())
          const rootResponse = await fetch(base)
          assert.equal(rootResponse.status, 200)
        })

        await t.test('missing static assets and Vite modules never fall back to an HTML document', async () => {
          for (const path of ['/assets/missing.js', '/assets/missing', '/src/missing', '/@vite/missing', '/node_modules/missing', '/missing.svg?x=1']) {
            const response = await fetch(new URL(path, base))
            assert.equal(response.status, 404, path)
            assert.doesNotMatch(await response.text(), /<!doctype html|<html/i, path)
          }
          const favicon = await fetch(new URL('/favicon.svg', base))
          assert.equal(favicon.status, 200)
          assert.match(favicon.headers.get('content-type') ?? '', /image\/svg\+xml/)
        })
      })
    }
  })
}
