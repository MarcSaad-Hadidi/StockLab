import assert from 'node:assert/strict'
import { beforeEach, test } from 'node:test'
import { registerHooks } from 'node:module'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { JSDOM } from 'jsdom'
import React, { act, StrictMode, useState } from 'react'

registerHooks({ load(url, context, nextLoad) {
  if (url.endsWith('.css')) return { format: 'module', shortCircuit: true, source: 'export default {}' }
  if (url.includes('/node_modules/') && (url.endsWith('.js') || url.endsWith('.cjs')) && context.format !== 'module')
    return { format: 'commonjs', shortCircuit: true, source: readFileSync(fileURLToPath(url), 'utf8') }
  return nextLoad(url, context)
} })
const dom = new JSDOM('', { url: 'http://localhost/dashboard' })
Object.assign(globalThis, { React, window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
const { createRoot } = await import('react-dom/client')
const { i18n } = await import('../src/i18n/i18n.ts')
const { Sidebar } = await import('../src/components/layout/Sidebar.tsx')
const { TopBar } = await import('../src/components/layout/TopBar.tsx')

let mobile = true
const mediaListeners = new Set<() => void>()
Object.defineProperty(window, 'matchMedia', { value: () => ({
  get matches() { return mobile },
  addEventListener: (_: string, callback: () => void) => mediaListeners.add(callback),
  removeEventListener: (_: string, callback: () => void) => mediaListeners.delete(callback),
}) })

beforeEach(async () => {
  mobile = true
  window.localStorage.clear()
  await i18n.changeLanguage('en')
})

function Shell() {
  const [open, setOpen] = useState(false)
  const [count, setCount] = useState(0)
  return React.createElement('div', { className: 'stocklab-layout' },
    React.createElement(Sidebar, { open, onClose: () => setOpen(false) }),
    React.createElement('main', null,
      React.createElement(TopBar, { title: 'Dashboard', onMenuOpen: () => setOpen(true) }),
      React.createElement('button', { id: 'background', onClick: () => setCount(count + 1) }, `Background ${count}`)))
}

async function mount() {
  const outside = document.createElement('button'); outside.textContent = 'Outside app'
  const preserved = document.createElement('div'); preserved.setAttribute('inert', ''); preserved.textContent = 'Already inert'
  const container = document.createElement('div')
  document.body.append(container, outside, preserved)
  const root = createRoot(container)
  const render = () => act(async () => root.render(React.createElement(StrictMode, null, React.createElement(Shell))))
  await render()
  const panel = () => container.querySelector('aside')!
  const toggle = container.querySelector<HTMLButtonElement>('.app-topbar-menu')!
  const first = () => panel().querySelector<HTMLButtonElement>('button[aria-label]')!
  const last = () => panel().querySelector<HTMLButtonElement>('button[title="Français"]')!
  const main = container.querySelector('main')!
  const key = async (key: string, shiftKey = false) => {
    const event = new dom.window.KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true })
    await act(async () => document.activeElement!.dispatchEvent(event))
    return event
  }
  return { container, outside, preserved, panel, toggle, first, last, main, key, render,
    async open() { await act(async () => { toggle.focus(); toggle.click() }) },
    async resize(isMobile: boolean) { await act(async () => { mobile = isMobile; for (const callback of [...mediaListeners]) callback() }) },
    async close() { await act(async () => root.unmount()); container.remove(); outside.remove(); preserved.remove() },
  }
}

for (const language of ['en', 'fr']) {
  test(`mobile opening focuses the menu, exposes its name and blocks the background in ${language}`, async () => {
    await i18n.changeLanguage(language)
    const view = await mount()
    try {
      assert.ok(view.panel().hasAttribute('inert'), 'closed mobile navigation is not interactive')
      assert.equal(view.main.hasAttribute('inert'), false)
      await view.open()
      assert.equal(view.toggle.getAttribute('aria-controls'), view.panel().id)
      assert.equal(view.panel().getAttribute('role'), 'dialog')
      assert.equal(view.panel().getAttribute('aria-modal'), 'true')
      assert.equal(view.panel().getAttribute('aria-label'), language === 'fr' ? 'Navigation principale' : 'Primary navigation')
      assert.ok(view.first(), 'there is a keyboard-accessible close button inside the menu')
      assert.equal(document.activeElement, view.first())
      assert.equal(view.panel().hasAttribute('inert'), false)
      assert.ok(view.main.hasAttribute('inert'))
      assert.ok(view.outside.hasAttribute('inert'))
    } finally { await view.close() }
  })
}

test('Tab and Shift+Tab wrap between the first and last menu controls', async () => {
  const view = await mount()
  try {
    await view.open()
    const backwards = await view.key('Tab', true)
    assert.ok(backwards.defaultPrevented)
    assert.equal(document.activeElement, view.last())
    const forwards = await view.key('Tab')
    assert.ok(forwards.defaultPrevented)
    assert.equal(document.activeElement, view.first())
    const link = view.panel().querySelector<HTMLAnchorElement>('a[href="/portfolio"]')!
    link.focus()
    assert.equal((await view.key('Tab')).defaultPrevented, false, 'normal tabbing inside the menu is preserved')
    assert.equal((await view.key('ArrowDown')).defaultPrevented, false)
  } finally { await view.close() }
})

test('focus redirected outside the menu is brought back without waiting for a key press', async () => {
  const view = await mount()
  try {
    await view.open()
    // JSDOM has no native inert focus blocking; the focus guard also handles this case.
    view.outside.focus()
    assert.ok(view.panel().contains(document.activeElement))
  } finally { await view.close() }
})

for (const method of ['escape', 'close button', 'backdrop', 'navigation link'] as const) {
  test(`${method} closes mobile navigation and restores the opener and background`, async () => {
    const view = await mount()
    try {
      await view.open()
      if (method === 'escape') await view.key('Escape')
      else await act(async () => {
        if (method === 'close button') view.first().click()
        else if (method === 'backdrop') view.container.querySelector<HTMLButtonElement>('button[tabindex="-1"]')!.click()
        else {
          const link = view.panel().querySelector<HTMLAnchorElement>('a[href="/portfolio"]')!
          link.addEventListener('click', event => event.preventDefault(), { once: true })
          link.click()
        }
      })
      assert.ok(view.panel().hasAttribute('inert'))
      assert.equal(view.panel().getAttribute('aria-modal'), null)
      assert.equal(document.activeElement, view.toggle)
      assert.equal(view.main.hasAttribute('inert'), false)
      assert.equal(view.outside.hasAttribute('inert'), false)
      assert.ok(view.preserved.hasAttribute('inert'), 'pre-existing inert state must survive')
      assert.equal((await view.key('Tab')).defaultPrevented, false, 'the trap was removed')
      await view.open()
      assert.equal(document.activeElement, view.first(), 'reopening starts a fresh focus cycle')
    } finally { await view.close() }
  })
}

test('page rerenders and language changes do not restart focus inside an open menu', async () => {
  const view = await mount()
  try {
    await view.open()
    view.last().focus()
    await view.render()
    assert.equal(document.activeElement, view.last())
    await act(async () => i18n.changeLanguage('fr'))
    assert.equal(document.activeElement, view.last())
    await view.key('Escape')
    assert.equal(document.activeElement, view.toggle)
  } finally { await view.close() }
})

test('desktop navigation never traps focus or blocks the page', async () => {
  mobile = false
  const view = await mount()
  try {
    await view.open()
    assert.equal(view.panel().getAttribute('role'), null)
    assert.equal(view.panel().hasAttribute('inert'), false)
    assert.equal(view.first(), null)
    assert.equal(view.main.hasAttribute('inert'), false)
    view.last().focus()
    assert.equal((await view.key('Tab')).defaultPrevented, false)
    view.outside.focus()
    assert.equal(document.activeElement, view.outside)
  } finally { await view.close() }
})

test('resizing an open mobile menu releases the desktop page and traps again on mobile', async () => {
  const view = await mount()
  try {
    await view.open()
    await view.resize(false)
    assert.equal(view.panel().getAttribute('aria-modal'), null)
    assert.equal(view.main.hasAttribute('inert'), false)
    assert.equal(view.outside.hasAttribute('inert'), false)
    view.outside.focus()
    assert.equal(document.activeElement, view.outside)
    assert.equal((await view.key('Tab')).defaultPrevented, false)
    await view.resize(true)
    assert.ok(view.main.hasAttribute('inert'))
    assert.equal(document.activeElement, view.first())
  } finally { await view.close() }
})

test('unmounting an open menu removes listeners and restores existing background states', async () => {
  const view = await mount()
  await view.open()
  await view.close()
  assert.equal(view.outside.hasAttribute('inert'), false)
  assert.ok(view.preserved.hasAttribute('inert'))
  assert.equal(mediaListeners.size, 0)
  const button = document.createElement('button'); document.body.append(button); button.focus()
  button.dispatchEvent(new dom.window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true }))
  assert.equal(document.activeElement, button)
  button.remove()
})
