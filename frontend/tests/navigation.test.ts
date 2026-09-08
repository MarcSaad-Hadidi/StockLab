import assert from 'node:assert/strict'
import { test } from 'node:test'
import { documentUrl, isCurrentPage, pageForPath, pages, routeFor } from '../src/navigation/routes.ts'
import { stockDetailsRoute } from '../src/market/marketRoutes.ts'

test('query strings and fragments do not hide the current page', () => {
  for (const [id, page] of Object.entries(pages)) {
    for (const path of [page.path, `${page.path}/`, `/${page.entry}`]) {
      const url = `${path}?symbol=aapl&return=%2Fportfolio%3Fx%3D1#details`
      assert.equal(pageForPath(url), id)
      assert.equal(isCurrentPage(id, url), true)
      assert.equal(documentUrl(url), `/${page.entry}?symbol=aapl&return=%2Fportfolio%3Fx%3D1`)
    }
  }
  assert.equal(pageForPath('/unknown?symbol=aapl'), undefined)
  assert.equal(isCurrentPage('market', '/unknown?symbol=aapl'), false)
})

test('stock details remain on Market and preserve encoded query parameters', () => {
  assert.equal(stockDetailsRoute('AAPL'), '/market?symbol=aapl')
  assert.equal(documentUrl(stockDetailsRoute('AAPL')), '/market.html?symbol=aapl')
  assert.equal(documentUrl('/market/?symbol=brk.b&tag=a%2Bb&tag=two+words'), '/market.html?symbol=brk.b&tag=a%2Bb&tag=two+words')
  assert.equal(isCurrentPage('Settings', '/profile/index.html?tab=account'), true)
  assert.equal(isCurrentPage('Logout', '/login/?next=%2Fmarket'), true)
  assert.equal(isCurrentPage('AI Trader', '/ai-trader/?tab=history'), true)
})

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
  for (const url of ['/', '/index.html', '/@vite/client', '/@react-refresh', '/src/main.tsx', '/assets/app.js', '/vite.svg', '/node_modules/.vite/deps/react.js?v=123', '/src/missing', '/assets/missing?raw', '/@id/react', '/missing.svg?x=1']) {
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
