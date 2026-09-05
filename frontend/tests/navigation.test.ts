import assert from 'node:assert/strict'
import { test } from 'node:test'
import { documentUrl, isCurrentPage, pages, routeFor } from '../src/navigation/routes.ts'

test('all pages support clean URLs, trailing slashes and existing HTML entry URLs', () => {
  for (const page of Object.values(pages)) {
    for (const url of [page.path, `${page.path}/`, `/${page.entry}`]) {
      assert.equal(documentUrl(`${url}?symbol=BRK.B`), `/${page.entry}?symbol=BRK.B`)
    }
  }
})

test('unknown nested routes reach Not Found without intercepting static files or Vite modules', () => {
  assert.equal(documentUrl('/this-page-does-not-exist'), '/not-found/index.html')
  assert.equal(documentUrl('/unknown/nested/?x=1'), '/not-found/index.html?x=1')
  for (const url of ['/', '/index.html', '/@vite/client', '/@react-refresh', '/src/main.tsx', '/assets/app.js', '/vite.svg']) {
    assert.equal(documentUrl(url), url)
  }
})

test('account aliases use existing pages without authentication', () => {
  assert.equal(routeFor('Settings'), '/profile')
  assert.equal(routeFor('Logout'), '/login')
  assert.equal(routeFor('AI Trader'), '/ai-trader')
})

test('active navigation recognizes clean and legacy paths', () => {
  assert.equal(isCurrentPage('Market', '/market.html'), true)
  assert.equal(isCurrentPage('Market', '/market/'), true)
  assert.equal(isCurrentPage('Dashboard', '/market'), false)
  assert.equal(isCurrentPage('Profile', '/missing'), false)
})
