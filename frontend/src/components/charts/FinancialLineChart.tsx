import { useId, useState } from 'react'
import { createPortal } from 'react-dom'
import { LineChart, lineClasses } from '@mui/x-charts/LineChart'
import { useDrawingArea, useXScale, useYScale } from '@mui/x-charts/hooks'
import './financial-line-chart.css'

type FinancialLineChartProps = {
  values: number[]
  labels: string[]
  min: number
  max: number
  ariaLabel: string
  formatValue: (value: number) => string
  formatTick?: (value: number) => string
  pointLabel?: (index: number) => string
  showLatestValue?: boolean
}

function NoTooltip() {
  return null
}

function ChartInteraction({ values, labels, formatValue, pointLabel, showLatestValue, ariaLabel, overlay }: FinancialLineChartProps & { overlay: HTMLDivElement | null }) {
  const xScale = useXScale<'point'>()
  const yScale = useYScale<'linear'>()
  const { left, top, width, height } = useDrawingArea()
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null)
  const [focusedIndex, setFocusedIndex] = useState<number | null>(null)
  const [pinnedIndex, setPinnedIndex] = useState<number | null>(null)
  const activeIndex = hoveredIndex ?? focusedIndex ?? pinnedIndex
  const markerIndex = activeIndex ?? values.length - 1
  const x = xScale(markerIndex) ?? left
  const y = yScale(values[markerIndex])
  const tooltipWidth = Math.min(activeIndex === null ? Math.max(96, formatValue(values[markerIndex]).length * 8 + 28) : 164, width + left - 8)
  const tooltipHeight = activeIndex === null ? 34 : 66
  const tooltipX = Math.min(Math.max(x - tooltipWidth / 2, 8), left + width - tooltipWidth)
  const tooltipY = Math.min(Math.max(y < top + tooltipHeight + 20 ? y + 20 : y - tooltipHeight - 20, 6), top + height - tooltipHeight)
  const announce = (index: number) => pointLabel?.(index) ?? `${labels[index]}: ${formatValue(values[index])}`

  // MUI's drawing layer is aria-hidden; keep the interactive points in an
  // accessible sibling SVG, using the exact same MUI scales and plot bounds.
  if (!overlay) return null
  return createPortal(<>
    <svg width="100%" height="100%" role="group" aria-label={ariaLabel}>
    <g className="financial-chart-interaction">
      {activeIndex !== null && <line className="financial-chart-crosshair" x1={x} x2={x} y1={top} y2={top + height} />}
      {values.map((value, index) => {
        const pointX = xScale(index) ?? left
        const pointY = yScale(value)
        const previousX = xScale(index - 1) ?? pointX
        const nextX = xScale(index + 1) ?? pointX
        const hitLeft = index === 0 ? left : (previousX + pointX) / 2
        const hitRight = index === values.length - 1 ? left + width : (pointX + nextX) / 2
        return (
          <g key={index} role="button" tabIndex={0} aria-label={announce(index)} aria-pressed={pinnedIndex === index}
            className={`financial-chart-point ${activeIndex === index ? 'active' : ''}`}
            onPointerEnter={() => setHoveredIndex(index)} onPointerLeave={() => setHoveredIndex(null)}
            onFocus={() => setFocusedIndex(index)} onBlur={() => setFocusedIndex(null)}
            onClick={() => setPinnedIndex(current => current === index ? null : index)}
            onKeyDown={event => {
              if (event.key === 'Escape') setPinnedIndex(null)
              if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault()
                setPinnedIndex(current => current === index ? null : index)
              }
            }}>
            <rect className="financial-chart-hit" x={hitLeft} y={top} width={Math.max(0, hitRight - hitLeft)} height={height} />
            {(activeIndex === index || index === values.length - 1) && <>
              <circle className="financial-chart-halo" cx={pointX} cy={pointY} r="10" />
              <circle className="financial-chart-dot" cx={pointX} cy={pointY} r="4.5" />
            </>}
          </g>
        )
      })}
      {(activeIndex !== null || showLatestValue) && <g className="financial-chart-tooltip" pointerEvents="none" transform={`translate(${tooltipX} ${tooltipY})`}>
        <rect width={tooltipWidth} height={tooltipHeight} rx="10" />
        {activeIndex !== null && <text className="financial-chart-date" x="14" y="23">{labels[markerIndex]}</text>}
        <text className="financial-chart-value" x="14" y={activeIndex === null ? 22 : 48}>{formatValue(values[markerIndex])}</text>
      </g>}
    </g>
    </svg>
    <span className="financial-chart-announcement" aria-live="polite">{activeIndex === null ? '' : announce(activeIndex)}</span>
  </>, overlay)
}

export function FinancialLineChart(props: FinancialLineChartProps) {
  const { values, labels, min, max, ariaLabel, formatValue, formatTick = formatValue } = props
  const gradientId = useId().replaceAll(':', '')
  const [overlay, setOverlay] = useState<HTMLDivElement | null>(null)
  return (
    <div className="financial-line-chart">
      <LineChart
        aria-label={ariaLabel}
        margin={{ left: 4, right: 28, top: 28, bottom: 12 }}
        xAxis={[{
          scaleType: 'point', data: values.map((_, index) => index),
          valueFormatter: index => labels[index] ?? '',
          disableLine: true, disableTicks: true, height: 32,
          tickLabelMinGap: 28, tickLabelStyle: { fontSize: 11, fill: '#74839a', fontFamily: 'inherit' },
        }]}
        yAxis={[{
          min, max, width: 78, tickNumber: 4, domainLimit: 'strict',
          valueFormatter: formatTick, disableLine: true, disableTicks: true,
          tickLabelStyle: { fontSize: 11, fill: '#74839a', fontFamily: 'inherit' },
        }]}
        series={[{
          id: 'value', data: values, color: '#2f7bf0', curve: 'monotoneX',
          area: true, baseline: min, showMark: false, disableHighlight: true,
          valueFormatter: value => value === null ? '' : formatValue(value),
        }]}
        grid={{ horizontal: true }}
        hideLegend
        skipAnimation
        disableKeyboardNavigation
        axisHighlight={{ x: 'none', y: 'none' }}
        slots={{ tooltip: NoTooltip }}
        sx={{
          [`& .${lineClasses.line}`]: { strokeWidth: 2.8, strokeLinecap: 'round', strokeLinejoin: 'round' },
          [`& .${lineClasses.area}`]: { fill: `url(#${gradientId})` },
          '& .MuiChartsGrid-line': { stroke: '#e8eef6', strokeDasharray: '3 6' },
        }}
      >
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#2f7bf0" stopOpacity="0.2" />
            <stop offset="95%" stopColor="#2f7bf0" stopOpacity="0.01" />
          </linearGradient>
        </defs>
        <ChartInteraction {...props} overlay={overlay} />
      </LineChart>
      <div className="financial-chart-overlay" ref={setOverlay} />
    </div>
  )
}
