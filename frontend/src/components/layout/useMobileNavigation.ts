import { useEffect, useEffectEvent, useRef, useSyncExternalStore } from 'react'

const mobileQuery = '(max-width: 760px)'
const isMobile = () => window.matchMedia?.(mobileQuery).matches ?? false
const subscribe = (onChange: () => void) => {
  const media = window.matchMedia?.(mobileQuery)
  media?.addEventListener('change', onChange)
  return () => media?.removeEventListener('change', onChange)
}

export function useMobileNavigation(open: boolean, onClose: () => void) {
  const panelRef = useRef<HTMLElement>(null)
  const backdropRef = useRef<HTMLButtonElement>(null)
  const mobile = useSyncExternalStore(subscribe, isMobile, () => false)
  const close = useEffectEvent(onClose)

  useEffect(() => {
    const panel = panelRef.current
    if (!mobile || !open || !panel) return

    const opener = panel.closest('.stocklab-layout')?.querySelector<HTMLButtonElement>('[aria-controls="stocklab-navigation"]')
    const focusable = () => Array.from(panel.querySelectorAll<HTMLElement>('a[href], button, [tabindex]'))
      .filter(element => element.tabIndex >= 0 && !element.matches(':disabled') && !element.closest('[hidden], [inert]')
        && window.getComputedStyle(element).display !== 'none' && window.getComputedStyle(element).visibility !== 'hidden')
    const focusFirst = () => (focusable()[0] ?? panel).focus({ preventScroll: true })

    // Isolate the menu even when its page is nested inside an entry-point root.
    const blocked: Element[] = []
    let branch: Element = panel
    while (branch.parentElement && branch !== document.body) {
      for (const sibling of branch.parentElement.children) {
        if (sibling !== branch && sibling !== backdropRef.current && !sibling.hasAttribute('inert')) {
          sibling.setAttribute('inert', '')
          blocked.push(sibling)
        }
      }
      branch = branch.parentElement
    }

    const onFocus = (event: FocusEvent) => {
      if (!panel.contains(event.target as Node)) focusFirst()
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        close()
      } else if (event.key === 'Tab') {
        const controls = focusable()
        const first = controls[0] ?? panel
        const last = controls.at(-1) ?? panel
        if (!panel.contains(document.activeElement) || document.activeElement === panel
          || document.activeElement === (event.shiftKey ? first : last)) {
          event.preventDefault()
          const target = event.shiftKey ? last : first
          target.focus()
        }
      }
    }

    document.addEventListener('keydown', onKey, true)
    document.addEventListener('focusin', onFocus)
    focusFirst()
    return () => {
      document.removeEventListener('keydown', onKey, true)
      document.removeEventListener('focusin', onFocus)
      for (const element of blocked) element.removeAttribute('inert')
      // The toggle is hidden on desktop; resizing must release the focus trap.
      if (opener?.isConnected && isMobile()) opener.focus({ preventScroll: true })
    }
  }, [mobile, open])

  return { panelRef, backdropRef, mobile }
}
