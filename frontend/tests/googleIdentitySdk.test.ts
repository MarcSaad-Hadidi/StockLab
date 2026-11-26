import assert from 'node:assert/strict'
import { test } from 'node:test'
import { JSDOM } from 'jsdom'
import { loadGoogleIdentitySdk } from '../src/auth/googleIdentitySdk.ts'

test('Google SDK loads the official script once and retries after a script failure', async () => {
  const dom = new JSDOM('', { url: 'https://stocklab.example/login' })
  Object.assign(globalThis, { window: dom.window, document: dom.window.document })
  const first = loadGoogleIdentitySdk()
  const second = loadGoogleIdentitySdk()
  assert.equal(first, second)
  const scripts = document.querySelectorAll('script')
  assert.equal(scripts.length, 1)
  assert.equal(scripts[0].src, 'https://accounts.google.com/gsi/client')
  scripts[0].dispatchEvent(new dom.window.Event('error'))
  await assert.rejects(first)
  const retry = loadGoogleIdentitySdk()
  const next = document.querySelector('script')!
  assert.notEqual(next, scripts[0])
  // An isolated fake SDK verifies integration behavior; application code never provides one.
  const oauth2 = { initCodeClient: () => ({ requestCode() {} }) }
  Object.assign(window, { google: { accounts: { oauth2 } } })
  next.dispatchEvent(new dom.window.Event('load'))
  assert.equal(await retry, oauth2)
  dom.window.close()
})

test('Google SDK times out without silently providing a replacement', async t => {
  const dom = new JSDOM('', { url: 'https://stocklab.example/login' })
  Object.assign(globalThis, { window: dom.window, document: dom.window.document })
  let expire!: () => void
  t.mock.method(window, 'setTimeout', callback => { expire = callback as () => void; return 1 })
  const pending = loadGoogleIdentitySdk()
  expire()
  await assert.rejects(pending)
  assert.equal(document.querySelectorAll('script').length, 0)
  assert.equal('google' in window, false)
  dom.window.close()
})
