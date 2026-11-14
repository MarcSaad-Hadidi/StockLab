import { useEffect, useEffectEvent, useRef } from 'react'

export function useOrderConfirmationFocus(open: boolean, submitting: boolean, onDismiss: () => void) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const dismiss = useEffectEvent(onDismiss)

  useEffect(() => {
    const dialog = dialogRef.current
    if (!open || !dialog) return

    const opener = document.activeElement as HTMLElement | null
    const controls = () => Array.from(dialog.querySelectorAll<HTMLButtonElement>('button'))
      .filter(element => !element.disabled && element.tabIndex >= 0 && !element.closest('[hidden], [inert]'))
    const focusFirst = () => (controls()[0] ?? dialog).focus({ preventScroll: true })

    // Isolate every background branch, including the shell's navigation and top bar.
    const blocked: Element[] = []
    let branch: Element = dialog
    while (branch.parentElement && branch !== document.body) {
      for (const sibling of branch.parentElement.children) {
        if (sibling !== branch && !sibling.hasAttribute('inert')) {
          sibling.setAttribute('inert', '')
          blocked.push(sibling)
        }
      }
      branch = branch.parentElement
    }

    const onFocus = (event: FocusEvent) => {
      if (!dialog.contains(event.target as Node)) focusFirst()
    }
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        dismiss()
      } else if (event.key === 'Tab') {
        const buttons = controls()
        const first = buttons[0] ?? dialog
        const last = buttons.at(-1) ?? dialog
        if (!dialog.contains(document.activeElement) || document.activeElement === dialog
          || document.activeElement === (event.shiftKey ? first : last)) {
          event.preventDefault()
          const target = event.shiftKey ? last : first
          target.focus({ preventScroll: true })
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
      if (opener?.isConnected && !opener.matches(':disabled') && !opener.closest('[inert], [hidden]')) {
        opener.focus({ preventScroll: true })
      }
    }
  }, [open])

  useEffect(() => {
    // All actions are disabled during execution; the dialog itself remains focusable.
    if (open && submitting) dialogRef.current?.focus({ preventScroll: true })
  }, [open, submitting])

  return dialogRef
}
