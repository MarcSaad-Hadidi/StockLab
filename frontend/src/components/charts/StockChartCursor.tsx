import { useEffect, useRef, useState, type PointerEvent } from 'react'
import { createPortal } from 'react-dom'
import { useDrawingArea, useXScale, useYScale } from '@mui/x-charts/hooks'

type Selection = { index: number; x: number; y: number }
type Props = {
  overlay: HTMLDivElement | null
  values: number[]
  labels: string[]
  ariaLabel: string
  keyboardInstructions: string
  formatValue: (value: number) => string
  showLatestValue?: boolean
}

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max)

export function StockChartCursor({ overlay, values, labels, ariaLabel, keyboardInstructions, formatValue, showLatestValue }: Props) {
  const xScale = useXScale<'point'>()
  const yScale = useYScale<'linear'>()
  const { left, top, width, height } = useDrawingArea()
  const [cursor, setCursor] = useState<Selection | null>(null)
  const [keyboard, setKeyboard] = useState<number | null>(null)
  const [pinned, setPinned] = useState<number | null>(null)
  const frame = useRef<number | null>(null)
  const pending = useRef<Selection | null>(null)
  const touchId = useRef<number | null>(null)
  const last = values.length - 1

  useEffect(() => () => {
    if (frame.current !== null) window.cancelAnimationFrame(frame.current)
  }, [])

  if (!overlay || last < 0 || width <= 0 || height <= 0) return null
  const xAt = (index: number) => xScale(index) ?? left
  const active = cursor?.index ?? keyboard ?? pinned
  const index = active ?? last
  const dotX = xAt(index)
  const dotY = yScale(values[index])
  const value = formatValue(values[index])
  const cursorX = cursor ? clamp(cursor.x, left, left + width) : dotX
  const cursorY = cursor ? clamp(cursor.y, top, top + height) : dotY
  const preferredWidth = Math.max(active === null ? 96 : 164,
    value.length * 9 + 28, active === null ? 0 : labels[index].length * 6.5 + 28)
  const tooltipWidth = Math.min(preferredWidth, left + width - 8)
  const tooltipHeight = active === null ? 34 : 66
  const tooltipX = active === null || !cursor
    ? cursorX - tooltipWidth / 2
    : cursorX + tooltipWidth + 14 <= left + width ? cursorX + 14 : cursorX - tooltipWidth - 14
  const tooltipY = cursorY - tooltipHeight - 16 < top ? cursorY + 16 : cursorY - tooltipHeight - 16

  function pick(event: PointerEvent<SVGRectElement>): Selection | null {
    const bounds = event.currentTarget.getBoundingClientRect()
    if (!bounds.width || !bounds.height) return null
    const x = left + clamp((event.clientX - bounds.left) / bounds.width, 0, 1) * width
    const y = top + clamp((event.clientY - bounds.top) / bounds.height, 0, 1) * height
    const firstX = xAt(0)
    // Stock Details retains the existing equally spaced observed bars. Find
    // the nearest available bar in constant time; gaps never create new data.
    const fraction = last === 0 ? 0 : (x - firstX) / (xAt(last) - firstX || 1)
    return { index: clamp(Math.round(fraction * last), 0, last), x, y }
  }

  function clearCursor() {
    if (frame.current !== null) window.cancelAnimationFrame(frame.current)
    frame.current = null
    pending.current = null
    setCursor(null)
  }

  function move(event: PointerEvent<SVGRectElement>) {
    if (event.pointerType === 'touch' && touchId.current !== event.pointerId) return
    pending.current = pick(event)
    if (frame.current !== null) return
    frame.current = window.requestAnimationFrame(() => {
      frame.current = null
      setKeyboard(null)
      setCursor(pending.current)
    })
  }

  const selected = keyboard ?? pinned ?? last
  const description = `${labels[selected]}: ${formatValue(values[selected])}`
  return createPortal(
    <svg width="100%" height="100%">
      {active !== null && <line className="financial-chart-crosshair" x1={cursorX} x2={cursorX} y1={top} y2={top + height} />}
      <rect className="financial-chart-hit stock-chart-cursor" x={left} y={top} width={width} height={height}
        tabIndex={0} role="slider" aria-label={ariaLabel} aria-description={keyboardInstructions}
        aria-valuemin={1} aria-valuemax={values.length} aria-valuenow={selected + 1} aria-valuetext={description}
        onPointerMove={move}
        onPointerLeave={() => { if (touchId.current === null) clearCursor() }}
        onPointerDown={event => {
          if (event.pointerType !== 'touch' || touchId.current !== null) return
          touchId.current = event.pointerId
          event.currentTarget.setPointerCapture?.(event.pointerId)
          move(event)
        }}
        onPointerUp={event => {
          if (event.pointerType === 'touch') {
            if (touchId.current !== event.pointerId) return
            touchId.current = null
            clearCursor()
          } else if (event.button !== 0) return
          const next = pick(event)
          if (next) {
            setKeyboard(null)
            setPinned(next.index)
          }
        }}
        onPointerCancel={event => {
          if (touchId.current !== event.pointerId) return
          touchId.current = null
          clearCursor()
        }}
        onLostPointerCapture={event => {
          if (touchId.current !== event.pointerId) return
          touchId.current = null
          clearCursor()
        }}
        onFocus={event => {
          if (event.currentTarget.matches(':focus-visible')) setKeyboard(pinned ?? last)
        }}
        onBlur={() => setKeyboard(null)}
        onKeyDown={event => {
          let next: number
          switch (event.key) {
            case 'ArrowLeft':
            case 'ArrowDown': next = selected - 1; break
            case 'ArrowRight':
            case 'ArrowUp': next = selected + 1; break
            case 'Home': next = 0; break
            case 'End': next = last; break
            case 'Escape':
              clearCursor()
              setKeyboard(null)
              setPinned(null)
              return
            default: return
          }
          event.preventDefault()
          clearCursor()
          setKeyboard(clamp(next, 0, last))
        }}
      />
      <circle className="financial-chart-halo" cx={dotX} cy={dotY} r="10" />
      <circle className="financial-chart-dot" cx={dotX} cy={dotY} r="4.5" />
      {(active !== null || showLatestValue) && <g className="financial-chart-tooltip" aria-hidden="true" pointerEvents="none"
        transform={`translate(${clamp(tooltipX, 8, left + width - tooltipWidth)} ${clamp(tooltipY, 6, top + height - tooltipHeight)})`}>
        <rect width={tooltipWidth} height={tooltipHeight} rx="10" />
        {active !== null && <text className="financial-chart-date" x="14" y="23">{labels[index]}</text>}
        <text className="financial-chart-value" x="14" y={active === null ? 22 : 48}>{value}</text>
      </g>}
    </svg>,
    overlay,
  )
}
