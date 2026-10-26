import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { JSDOM } from 'jsdom'
import { createPortfolioApi } from '../src/api/portfolioApi.ts'
import { createProfileApi } from '../src/api/profileApi.ts'
import { createWatchlistApi } from '../src/api/watchlistApi.ts'
import { createAiTraderApi } from '../src/api/aiTraderApi.ts'
import { createPasswordApi } from '../src/api/passwordApi.ts'
import { createTradingApi } from '../src/api/tradingApi.ts'
import { createAuthApi } from '../src/api/authApi.ts'
import { authStorageKey, getAuthSession, saveAuthSession } from '../src/auth/authStorage.ts'

const dom = new JSDOM('', { url: 'http://localhost/profile' })
Object.assign(globalThis, { window: dom.window, document: dom.window.document })
const session = { accessToken: 'token-a', tokenType: 'Bearer' as const, expiresAtUtc: '2099-01-01T00:00:00Z',
  user: { id: 'account-a', displayName: 'Account A', email: 'a@example.test' } }
const clients = {
  portfolio: (fetcher: typeof fetch, signal?: AbortSignal) => createPortfolioApi('', fetcher).getPortfolio(signal),
  transactions: (fetcher: typeof fetch, signal?: AbortSignal) => createPortfolioApi('', fetcher).getTransactionHistory({ page: 1, pageSize: 10 }, signal),
  recent: (fetcher: typeof fetch, signal?: AbortSignal) => createPortfolioApi('', fetcher).getRecentTransactions(5, signal),
  profile: (fetcher: typeof fetch, signal?: AbortSignal) => createProfileApi('', fetcher).getProfile(signal),
  profileSave: (fetcher: typeof fetch, signal?: AbortSignal) => createProfileApi('', fetcher).updateProfile({ displayName: 'Account A', email: 'a@example.test' }, signal),
  watchlist: (fetcher: typeof fetch, signal?: AbortSignal) => createWatchlistApi('', fetcher).getWatchlist(signal),
  watchlistAdd: (fetcher: typeof fetch, signal?: AbortSignal) => createWatchlistApi('', fetcher).addToWatchlist('AAPL', signal),
  watchlistRemove: (fetcher: typeof fetch, signal?: AbortSignal) => createWatchlistApi('', fetcher).removeFromWatchlist('AAPL', signal),
  aiTrader: (fetcher: typeof fetch, signal?: AbortSignal) => createAiTraderApi('', fetcher).getPerformance(signal),
  password: (fetcher: typeof fetch, signal?: AbortSignal) => createPasswordApi('', fetcher).changePassword({ currentPassword: 'Old-password1!', newPassword: 'New-password2!', confirmPassword: 'New-password2!' }, signal),
  trade: (fetcher: typeof fetch, signal?: AbortSignal) => createTradingApi('', fetcher).executeTrade({ orderId: '5c9e50f8-92b0-4eac-99c6-263d54d0bb15', symbol: 'AAPL', side: 'BUY', quantity: '1', orderType: 'market' }, signal),
}
beforeEach(() => { window.localStorage.clear(); saveAuthSession(session) })

for (const [name, request] of Object.entries(clients)) {
  test(`${name} rejects and clears the current session on HTTP 401, even without JSON`, async () => {
    window.localStorage.setItem('stocklab-language', 'fr')
    await assert.rejects(request(async (_url, init) => {
      assert.equal((init!.headers as Record<string, string>).Authorization, 'Bearer token-a')
      return new Response('Unauthorized', { status: 401 })
    }))
    assert.equal(getAuthSession(), null)
    assert.equal(window.localStorage.getItem(authStorageKey), null)
    assert.equal(window.localStorage.getItem('stocklab-language'), 'fr')
  })

  for (const replacement of ['same account', 'other account']) {
    test(`${name} cannot invalidate a ${replacement} login with a late old-token 401`, async () => {
      const next = { ...session, accessToken: 'token-b', user: replacement === 'same account' ? session.user
        : { id: 'account-b', displayName: 'Account B', email: 'b@example.test' } }
      await assert.rejects(request(async () => { saveAuthSession(next); return new Response(null, { status: 401 }) }))
      assert.deepEqual(getAuthSession(), next)
    })
  }

  test(`${name} ignores an aborted request's late 401`, async () => {
    const controller = new AbortController()
    await assert.rejects(request(async () => { controller.abort(); return new Response(null, { status: 401 }) }, controller.signal))
    assert.deepEqual(getAuthSession(), session)
  })
}

for (const status of [403, 429, 500, 502, 503]) {
  test(`HTTP ${status} does not invalidate the current login`, async () => {
    for (const request of Object.values(clients)) {
      await assert.rejects(request(async () => new Response(null, { status })))
      assert.deepEqual(getAuthSession(), session)
    }
  })
}

test('a login with invalid credentials never invalidates an existing session', async () => {
  await assert.rejects(createAuthApi('', async () => new Response(null, { status: 401 })).login({ email: 'wrong@example.test', password: 'wrong' }))
  assert.deepEqual(getAuthSession(), session)
})
