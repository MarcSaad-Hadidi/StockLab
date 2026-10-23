import { useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { AiTraderApi, AiTraderDecisionResponse, AiTraderSignal, AiTraderTradeResponse } from '../api/aiTraderApi'
import { PerformanceLineChart } from '../components/charts/PerformanceLineChart'
import { StockLogo } from '../market/StockLogo'
import { formatDate, formatNumber, formatPercent, formatQuantity, formatSignedPercent, formatUnitPrice } from '../i18n/formatters'
import { formatAiCurrency as formatCurrency, formatAiSignedCurrency as formatSignedCurrency, equityInRange, formatAiTimestamp, fractionPoints, positionPnlPercent, ranges, rejectionReasons, type EquityRange } from './aiTraderData'
import { useAiTraderDetail, type AiTraderDataState, type ResourceState } from './useAiTraderData'
import { routeFor } from '../navigation/routes'

type DataProps = Pick<AiTraderDataState, 'resources' | 'retry'>
export type DetailSelection = { kind: 'trade' | 'decision'; id: string }
type SelectDetail = (selection: DetailSelection) => void

export function PanelHeading({ title, subtitle, action, onAction }: { title: string; subtitle?: string; action?: string; onAction?: () => void }) {
  return <div className="panel-heading"><div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>{action && <button className="text-action" onClick={onAction} type="button">{action} ›</button>}</div>
}
export function ActionBadge({ action }: { action: AiTraderSignal }) {
  const { t } = useTranslation()
  return <span className={`action-badge action-${action.toLowerCase()}`}>{t(`aiTrader.actions.${action.toLowerCase()}`)}</span>
}
export function ResourceContent<T>({ resource, emptyKey, retry, catalog, tableHeadings, children }: {
  resource: ResourceState<T>; emptyKey?: string; retry: () => void; catalog?: 'models' | 'backtests'; tableHeadings?: string[]; children: (data: T) => ReactNode
}) {
  const { t } = useTranslation()
  if (resource.status === 'idle' || resource.status === 'loading') return <div className="ai-resource-state" role="status" aria-busy="true">{t('aiTrader.loading')}</div>
  if (resource.error || resource.status === 'unauthorized') {
    const code = resource.error?.code ?? 'unauthorized'
    const key = resource.error?.portfolioNotInitialized ? 'aiTrader.portfolioNotInitialized'
      : catalog && code === 'unavailable' ? `aiTrader.${catalog}Unavailable` : `aiTrader.errors.${code}`
    return <div className="ai-resource-state" role="alert"><p>{t(key)}</p>{code === 'unauthorized'
      ? <a href={routeFor('login')}>{t('aiTrader.signIn')}</a>
      : <button className="outline-button" onClick={retry} type="button">{t('aiTrader.retry')}</button>}</div>
  }
  if (resource.status === 'empty') {
    const message = <div className="ai-resource-state" role="status">{t(emptyKey ?? 'aiTrader.noData')}</div>
    return tableHeadings ? <Table headings={tableHeadings}><tr><td colSpan={tableHeadings.length}>{message}</td></tr></Table> : message
  }
  return resource.data === null ? null : <>{children(resource.data)}</>
}
function RejectionReason({ code }: { code: string | null }) {
  const { t } = useTranslation()
  return code === null ? '—' : t(rejectionReasons.some(reason => reason === code) ? `aiTrader.reasons.${code}` : 'aiTrader.reasons.unknown')
}
function Table({ headings, children }: { headings: string[]; children: ReactNode }) {
  const { t } = useTranslation()
  return <div className="table-scroll" tabIndex={0} role="region" aria-label={t('aiTrader.tableRegion')}><table className="data-table"><thead><tr>{headings.map(key => <th key={key} scope="col">{t(`aiTrader.${key}`)}</th>)}</tr></thead><tbody>{children}</tbody></table></div>
}
export function PositionTable({ resources, retry, compact = false }: DataProps & { compact?: boolean }) {
  const { i18n } = useTranslation()
  const currency = resources.performance.data?.currency ?? 'USD' // #80 positions are system-owned USD in V1.
  const money = (value: string) => formatCurrency(value, i18n.language, 2, currency)
  return <ResourceContent resource={resources.positions} emptyKey="aiTrader.noPositions" tableHeadings={['table.asset', 'table.size', 'table.entryPrice', 'table.currentPrice', 'marketValue', 'unrealizedPnl', 'table.pnlPercent']} retry={() => retry('positions')}>{rows =>
    <Table headings={['table.asset', 'table.size', 'table.entryPrice', 'table.currentPrice', 'marketValue', 'unrealizedPnl', 'table.pnlPercent']}>
      {(compact ? rows.slice(0, 5) : rows).map(position => <tr key={position.symbol}>
        <td><div className="asset-cell"><StockLogo symbol={position.symbol} /><strong>{position.symbol}</strong></div></td>
        <td>{formatQuantity(position.quantity, i18n.language)}</td><td>{money(position.averagePrice)}</td>
        <td>{formatUnitPrice(position.currentPrice, i18n.language, currency)}</td><td>{money(position.marketValue)}</td>
        <td className={position.pnL.startsWith('-') ? 'negative' : 'positive'}>{formatSignedCurrency(position.pnL, i18n.language, 2, currency)}</td>
        <td>{formatSignedPercent(positionPnlPercent(position), i18n.language)}</td>
      </tr>)}
    </Table>}</ResourceContent>
}
export function DecisionTable({ resources, retry, compact = false, selectDetail }: DataProps & { compact?: boolean; selectDetail: SelectDetail }) {
  const { t, i18n } = useTranslation()
  return <ResourceContent resource={resources.decisions} emptyKey="aiTrader.noDecisions" tableHeadings={['table.asset', 'table.action', 'table.confidence', 'status', 'decisionDate', 'modelLabel', 'modelVersion', 'details']} retry={() => retry('decisions')}>{rows =>
    <Table headings={['table.asset', 'table.action', 'table.confidence', 'status', 'decisionDate', 'modelLabel', 'modelVersion', 'details']}>
      {(compact ? rows.slice(0, 4) : rows).map(decision => <tr key={decision.decisionId}>
        <td>{decision.symbol}</td><td><ActionBadge action={decision.signal} /></td><td>{formatPercent(fractionPoints(decision.confidence), i18n.language, 1)}</td>
        <td><span className="execution-status">{t(`aiTrader.outcomes.${decision.executionStatus}`)}</span></td><td>{formatDate(decision.decisionDate, i18n.language)}</td>
        <td>{decision.modelName}</td><td>{decision.modelVersion}</td><td><button className="text-action" data-decision-id={decision.decisionId} onClick={() => selectDetail({ kind: 'decision', id: decision.decisionId })} type="button">{t('aiTrader.details')}</button></td>
      </tr>)}
    </Table>}</ResourceContent>
}
export function RejectedTable({ resources, retry, compact = false }: DataProps & { compact?: boolean }) {
  const { i18n } = useTranslation()
  return <ResourceContent resource={resources.rejections} emptyKey="aiTrader.noRejections" tableHeadings={['table.asset', 'table.action', 'table.reason', 'table.confidence', 'rejectedAt', 'modelVersion']} retry={() => retry('rejections')}>{rows =>
    <Table headings={['table.asset', 'table.action', 'table.reason', 'table.confidence', 'rejectedAt', 'modelVersion']}>
      {(compact ? rows.slice(0, 4) : rows).map(decision => <tr key={`${decision.decisionId}-${decision.rejectedAtUtc}`}>
        <td>{decision.symbol}</td><td><ActionBadge action={decision.signal} /></td><td><RejectionReason code={decision.rejectionReason} /></td>
        <td>{formatPercent(fractionPoints(decision.confidence), i18n.language, 1)}</td><td>{formatAiTimestamp(decision.rejectedAtUtc, i18n.language)}</td><td>{decision.modelName} · {decision.modelVersion}</td>
      </tr>)}
    </Table>}</ResourceContent>
}
export function TradesTable({ resources, retry, compact = false, selectDetail }: DataProps & { compact?: boolean; selectDetail: SelectDetail }) {
  const { t, i18n } = useTranslation()
  const currency = resources.performance.data?.currency ?? 'USD'
  return <ResourceContent resource={resources.trades} emptyKey="aiTrader.noTrades" tableHeadings={['tradeId', 'table.asset', 'table.side', 'table.size', 'executionPrice', 'totalAmount', 'realizedPnl', 'table.time', 'modelVersion', 'details']} retry={() => retry('trades')}>{rows =>
    <><Table headings={['tradeId', 'table.asset', 'table.side', 'table.size', 'executionPrice', 'totalAmount', 'realizedPnl', 'table.time', 'modelVersion', 'details']}>
      {(compact ? rows.slice(0, 4) : rows).map(trade => <tr key={trade.tradeId}>
        <td title={trade.tradeId}>{trade.tradeId}</td><td>{trade.symbol}</td><td><ActionBadge action={trade.side} /></td>
        <td>{formatQuantity(trade.quantity, i18n.language)}</td><td>{formatUnitPrice(trade.executionPrice, i18n.language, currency)}</td>
        <td>{formatCurrency(trade.totalAmount, i18n.language, 2, currency)}</td><td>{formatSignedCurrency(trade.realizedPnL, i18n.language, 2, currency)}</td>
        <td>{formatAiTimestamp(trade.executedAtUtc, i18n.language)}</td><td>{trade.decision ? `${trade.decision.modelName} · ${trade.decision.modelVersion}` : '—'}</td>
        <td><button className="text-action" data-trade-id={trade.tradeId} onClick={() => selectDetail({ kind: 'trade', id: trade.tradeId })} type="button">{t('aiTrader.details')}</button></td>
      </tr>)}
    </Table>{!compact && <p className="ai-coverage-note">{t('aiTrader.recentLimit', { count: 200 })}</p>}</>}
  </ResourceContent>
}
function Metrics({ entries }: { entries: Array<[string, ReactNode]> }) {
  const { t } = useTranslation()
  return <dl className="ai-metrics">{entries.map(([key, value]) => <div key={key}><dt>{t(`aiTrader.${key}`)}</dt><dd>{value}</dd></div>)}</dl>
}
export function PerformanceStatsPanel({ resources, retry }: DataProps) {
  const { t, i18n } = useTranslation()
  const language = i18n.language
  return <section className="panel stats-panel"><PanelHeading title={t('aiTrader.performanceStatistics')} subtitle={t('aiTrader.recentBotPerformance')} />
    <ResourceContent resource={resources.performance} retry={() => retry('performance')}>{data => {
      const money = (value: string | null) => formatSignedCurrency(value, language, 2, data.currency)
      const realized = (trade: typeof data.bestTrade) => trade ? `${trade.symbol} · ${money(trade.realizedPnL)} · ${formatQuantity(trade.quantity, language)} · ${formatAiTimestamp(trade.executedAtUtc, language)}` : '—'
      return <><Metrics entries={[
        ['totalReturn', formatSignedPercent(fractionPoints(data.totalReturn), language)], ['winRate', formatPercent(fractionPoints(data.winRate), language, 1)],
        ['averageWin', money(data.averageWin)], ['averageLoss', money(data.averageLoss)], ['totalTrades', formatNumber(data.executedTradeCount, language, 0)],
        ['bestTrade', realized(data.bestTrade)], ['worstTrade', realized(data.worstTrade)], ['maxDrawdown', formatPercent(fractionPoints(data.maxDrawdown), language)],
        ['realizedPnl', money(data.realizedPnL)], ['unrealizedPnl', money(data.unrealizedPnL)],
        ['buyTrades', formatNumber(data.buyTradeCount, language, 0)], ['sellTrades', formatNumber(data.sellTradeCount, language, 0)],
      ]} />
      {!data.tradeStatisticsComplete && <p className="ai-coverage-note" role="status">{t('aiTrader.incompleteStatistics', { count: data.unvaluedLegacySellCount })}</p>}
      <div className="ai-coverage-note"><strong>{t('aiTrader.historicalCoverage')}</strong><p>{t(`aiTrader.coverage.${data.historicalCoverage.status}`)}</p>
        <p>{t('aiTrader.sampleCount', { count: data.historicalCoverage.valuationSampleCount })} · {formatAiTimestamp(data.historicalCoverage.valuationFirstAtUtc, language)} – {formatAiTimestamp(data.historicalCoverage.valuationLastAtUtc, language)}</p>
        <p>{t('aiTrader.sampledDrawdown')}</p><p>{t('aiTrader.valuedAt', { time: formatAiTimestamp(data.valuationTimestampUtc, language) })}</p></div></>
    }}</ResourceContent></section>
}
export function EquityPanel({ resources, retry, range, setRange }: DataProps & { range: EquityRange; setRange: (value: EquityRange) => void }) {
  const { t, i18n } = useTranslation()
  const rows = equityInRange(resources.history.data ?? [], range)
  const currency = resources.history.data?.[0]?.currency ?? resources.performance.data?.currency ?? 'USD'
  return <section className="panel performance-panel"><PanelHeading title={t('dashboard.performanceTitle')} subtitle={t('aiTrader.portfolioValueOverTime')} />
    <div className="range-tabs" role="tablist" aria-label={t('aiTrader.timeRange')}>{ranges.map((value, index) => <button key={value} id={`ai-range-${value}`} role="tab" tabIndex={range === value ? 0 : -1} className={range === value ? 'selected' : ''} aria-selected={range === value} aria-controls="ai-equity-series" onClick={() => setRange(value)} onKeyDown={event => {
      const next = event.key === 'ArrowRight' ? ranges[(index + 1) % ranges.length] : event.key === 'ArrowLeft' ? ranges[(index + ranges.length - 1) % ranges.length] : event.key === 'Home' ? ranges[0] : event.key === 'End' ? ranges[ranges.length - 1] : null
      if (next) { event.preventDefault(); setRange(next); document.getElementById(`ai-range-${next}`)?.focus() }
    }} type="button">{t(`common.timeRanges.${value}`)}</button>)}</div>
    <div id="ai-equity-series" role="tabpanel" aria-labelledby={`ai-range-${range}`}>
    <ResourceContent resource={resources.history} emptyKey="aiTrader.noHistory" retry={() => retry('history')}>{() => rows.length === 0
      ? <p className="ai-resource-state" role="status">{t('aiTrader.noHistoryInRange')}</p>
      : <PerformanceLineChart values={rows.map(row => Number(row.totalValue))} labels={rows.map(row => formatAiTimestamp(row.valuedAtUtc, i18n.language))}
          ariaLabel={t('aiTrader.performanceChart')} formatValue={value => formatCurrency(value, i18n.language, 2, currency)}
          formatTick={value => formatCurrency(value, i18n.language, 0, currency)}
          pointLabel={index => t('aiTrader.chartPoint', { label: formatAiTimestamp(rows[index].valuedAtUtc, i18n.language), value: formatCurrency(rows[index].totalValue, i18n.language, 2, currency) })}
          unavailableMessage="aiTrader.noHistory" curve="linear" size="compact" />}</ResourceContent></div>
    <p className="ai-coverage-note">{t('aiTrader.historyLimit')}</p>
  </section>
}
export function ModelPanel({ resources, retry, detailed = false, onDetails }: DataProps & { detailed?: boolean; onDetails?: () => void }) {
  const { t, i18n } = useTranslation()
  const groups = new Map<string, NonNullable<typeof resources.models.data>>()
  for (const model of resources.models.data ?? []) groups.set(model.modelName, [...(groups.get(model.modelName) ?? []), model])
  return <section className="panel model-panel"><PanelHeading title={t('aiTrader.modelVersion')} subtitle={t('aiTrader.modelCatalog')} />
    <ResourceContent resource={resources.models} catalog="models" emptyKey="aiTrader.noModels" retry={() => retry('models')}>{() => <>
      {[...groups].map(([family, models]) => <section className="ai-model-family" key={family}><h3>{family}</h3>{models.map(model => <article className="ai-model-version" key={`${model.modelName}-${model.modelVersion}`}>
        <div className="model-version-row"><strong>{model.modelVersion}</strong><span className={model.status === 'Active' ? 'current-badge' : 'execution-status'}>{t(`aiTrader.modelStatuses.${model.status}`)}</span><small>{model.modelType}</small></div>
        <Metrics entries={[
          ['accuracy', formatPercent(fractionPoints(model.accuracy), i18n.language, 1)], ['precision', formatPercent(fractionPoints(model.precision), i18n.language, 1)],
          ['recall', formatPercent(fractionPoints(model.recall), i18n.language, 1)], ['f1Score', formatPercent(fractionPoints(model.f1), i18n.language, 1)],
          ['trainedOn', formatAiTimestamp(model.trainedAtUtc, i18n.language)], ['evaluationPeriod', `${formatDate(model.evaluationPeriod.startDate, i18n.language)} – ${formatDate(model.evaluationPeriod.endDate, i18n.language)}`],
          ...(detailed ? [['trainingPeriod', `${formatDate(model.trainingPeriod.startDate, i18n.language)} – ${formatDate(model.trainingPeriod.endDate, i18n.language)}`], ['trainingRunId', model.trainingRunId]] as Array<[string, ReactNode]> : []),
        ]} /></article>)}</section>)}<p className="ai-coverage-note">{t('aiTrader.familyStatusNote')}</p>
    </>}</ResourceContent>
    {!detailed && <button className="outline-button" disabled={!resources.models.data?.length} onClick={onDetails} type="button">{t('aiTrader.viewModelDetails')}</button>}
  </section>
}
export function BacktestPanel({ resources, retry, detailed = false, onDetails }: DataProps & { detailed?: boolean; onDetails?: () => void }) {
  const { t, i18n } = useTranslation()
  const [selectedIndex, setSelectedIndex] = useState(0)
  return <section className="panel backtest-panel"><PanelHeading title={t('aiTrader.backtestingResults')} subtitle={t('aiTrader.historicalSimulation')} />
    <ResourceContent resource={resources.backtests} catalog="backtests" emptyKey="aiTrader.noBacktests" retry={() => retry('backtests')}>{rows => {
      const selected = rows[selectedIndex] ?? rows[0]
      const percent = (value: number | null, signed = false) => (signed ? formatSignedPercent : formatPercent)(fractionPoints(value), i18n.language)
      return <><label className="ai-backtest-select">{t('aiTrader.selectBacktest')}<select data-backtests value={rows[selectedIndex] ? selectedIndex : 0} onChange={event => setSelectedIndex(Number(event.target.value))}>
        {rows.map((result, index) => <option key={index} value={index}>{result.modelName} · {result.modelVersion ?? t('aiTrader.versionUnavailable')} · {formatDate(result.startDate, i18n.language)} – {formatDate(result.endDate, i18n.language)} · {t(`aiTrader.sources.${result.evaluationSource}`)}</option>)}
      </select></label><Metrics entries={[
        ['modelLabel', selected.modelName], ['modelVersion', selected.modelVersion ?? t('aiTrader.versionUnavailable')],
        ['period', `${formatDate(selected.startDate, i18n.language)} – ${formatDate(selected.endDate, i18n.language)}`],
        ['initialCapital', formatCurrency(selected.initialCapital, i18n.language)], ['finalValue', formatCurrency(selected.finalValue, i18n.language)],
        ['totalReturn', percent(selected.totalReturn, true)], ['winRate', percent(selected.winRate)], ['maxDrawdown', percent(selected.maxDrawdown)],
        ['totalTrades', formatNumber(selected.executedTrades, i18n.language, 0)], ['benchmarkReturn', percent(selected.benchmarkReturn, true)],
        ['benchmarkMaxDrawdown', percent(selected.benchmarkMaxDrawdown)], ['returnDelta', percent(selected.returnDelta, true)],
        ['evaluationSource', t(`aiTrader.sources.${selected.evaluationSource}`)], ['sourceTimestamp', formatAiTimestamp(selected.sourceTimestampUtc, i18n.language)],
      ]} /></>
    }}</ResourceContent>
    <p className="ai-coverage-note">{t('aiTrader.backtestReadOnly')}</p>
    {detailed ? <button className="outline-button" disabled title={t('aiTrader.backtestReadOnly')} type="button">{t('aiTrader.runNewBacktest')}</button>
      : <button className="outline-button" disabled={!resources.backtests.data?.length} onClick={onDetails} type="button">{t('aiTrader.viewFullBacktest')}</button>}
  </section>
}
export function DetailPanel({ selection, api, close, selectDetail, rejectSession }: { selection: DetailSelection; api: AiTraderApi; close: () => void; selectDetail: SelectDetail; rejectSession: () => void }) {
  const { t, i18n } = useTranslation()
  const detail = useAiTraderDetail(selection.kind, selection.id, api, rejectSession)
  return <section className="panel ai-details" data-details aria-label={t('aiTrader.details')}><PanelHeading title={t(selection.kind === 'trade' ? 'aiTrader.tradeDetails' : 'aiTrader.decisionDetails')} action={t('aiTrader.closeDetails')} onAction={close} />
    <ResourceContent resource={detail} retry={detail.retry}>{data => {
      const trade = 'orderId' in data ? data as AiTraderTradeResponse : null
      const decision = 'executionStatus' in data ? data as AiTraderDecisionResponse : trade?.decision
      return <><Metrics entries={[
        ['table.asset', data.symbol], ['modelLabel', decision?.modelName ?? '—'], ['modelVersion', decision?.modelVersion ?? '—'],
        ['decisionDate', decision ? formatDate(decision.decisionDate, i18n.language) : '—'],
        ['table.confidence', decision ? formatPercent(fractionPoints(decision.confidence), i18n.language, 1) : '—'],
        ...(trade ? [['tradeId', trade.tradeId], ['orderId', trade.orderId], ['table.side', <ActionBadge action={trade.side} />], ['table.size', formatQuantity(trade.quantity, i18n.language)], ['executionPrice', formatUnitPrice(trade.executionPrice, i18n.language)], ['totalAmount', formatCurrency(trade.totalAmount, i18n.language)], ['realizedPnl', formatSignedCurrency(trade.realizedPnL, i18n.language)], ['table.time', formatAiTimestamp(trade.executedAtUtc, i18n.language)]] as Array<[string, ReactNode]> : []),
        ...(decision ? [['table.action', <ActionBadge action={decision.signal} />], ['decisionId', decision.decisionId]] as Array<[string, ReactNode]> : []),
        ...('executionStatus' in data ? [['status', t(`aiTrader.outcomes.${data.executionStatus}`)], ['table.reason', <RejectionReason code={data.rejectionReason} />], ['recordedAt', formatAiTimestamp(data.recordedAtUtc, i18n.language)]] as Array<[string, ReactNode]> : []),
      ]} />
      {trade?.decisionId && <button className="outline-button" data-decision-id={trade.decisionId} onClick={() => selectDetail({ kind: 'decision', id: trade.decisionId! })} type="button">{t('aiTrader.decisionDetails')}</button>}
      {'tradeId' in data && data.tradeId && !trade && <button className="outline-button" onClick={() => selectDetail({ kind: 'trade', id: data.tradeId! })} type="button">{t('aiTrader.tradeDetails')}</button>}</>
    }}</ResourceContent>
  </section>
}
