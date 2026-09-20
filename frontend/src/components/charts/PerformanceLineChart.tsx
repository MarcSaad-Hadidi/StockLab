import { UnavailableState } from '../UnavailableState'
import { FinancialLineChart } from './FinancialLineChart'

type PerformanceLineChartProps = {
  values: readonly number[]
  labels: readonly string[]
  ariaLabel: string
  formatValue: (value: number) => string
  formatTick?: (value: number) => string
  pointLabel?: (index: number) => string
  unavailableMessage: string
  showLatestValue?: boolean
  size?: 'default' | 'compact'
}

function chartDomain(values: readonly number[]) {
  const minValue = Math.min(...values)
  const maxValue = Math.max(...values)
  const span = maxValue - minValue
  const padding = span > 0 ? span * 0.01 : Math.max(Math.abs(maxValue) * 0.01, 1)
  return { min: minValue - padding, max: maxValue + padding }
}

function hasUsableSeries(values: readonly number[], labels: readonly string[]) {
  return values.length > 0 && values.length === labels.length && values.every(Number.isFinite)
}

/**
 * Shared account-performance chart entry point. It keeps empty account data
 * honest while ensuring every populated series uses FinancialLineChart.
 */
export function PerformanceLineChart({
  values,
  labels,
  ariaLabel,
  formatValue,
  formatTick,
  pointLabel,
  unavailableMessage,
  showLatestValue = false,
  size = 'default',
}: PerformanceLineChartProps) {
  if (!hasUsableSeries(values, labels)) {
    return (
      <div className={`financial-chart-state ${size === 'compact' ? 'financial-chart-state-compact' : ''}`}>
        <UnavailableState message={unavailableMessage} />
      </div>
    )
  }

  const { min, max } = chartDomain(values)
  return (
    <FinancialLineChart
      ariaLabel={ariaLabel}
      formatTick={formatTick}
      formatValue={formatValue}
      labels={[...labels]}
      max={max}
      min={min}
      pointLabel={pointLabel}
      showLatestValue={showLatestValue}
      size={size}
      values={[...values]}
    />
  )
}
