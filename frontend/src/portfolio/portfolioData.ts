export type Position = {
  symbol: string
  name: string
  quantity: number | null
  averagePrice: number | null
  currentPrice: number
  marketValue: number
  pnl: number | null
  pnlPercent: number | null
  weight: number
  tone: string
}

export const positions: Position[] = [
  { symbol: 'AAPL', name: 'Apple Inc.', quantity: 120, averagePrice: 175.22, currentPrice: 191.45, marketValue: 22_974, pnl: 1_947.60, pnlPercent: 9.26, weight: 17.9, tone: 'apple' },
  { symbol: 'MSFT', name: 'Microsoft Corp.', quantity: 85, averagePrice: 378.05, currentPrice: 415.60, marketValue: 35_326, pnl: 3_186.75, pnlPercent: 9.92, weight: 27.5, tone: 'microsoft' },
  { symbol: 'NVDA', name: 'NVIDIA Corp.', quantity: 53, averagePrice: 437.84, currentPrice: 892.72, marketValue: 47_286.16, pnl: 24_094.64, pnlPercent: 103.28, weight: 36.8, tone: 'nvidia' },
  { symbol: 'TSLA', name: 'Tesla Inc.', quantity: 35, averagePrice: 225.10, currentPrice: 154.32, marketValue: 5_401.20, pnl: -2_477.30, pnlPercent: -31.43, weight: 4.2, tone: 'tesla' },
  { symbol: 'AMZN', name: 'Amazon.com Inc.', quantity: 10, averagePrice: 181.42, currentPrice: 186.21, marketValue: 1_862.10, pnl: 47.90, pnlPercent: 2.64, weight: 1.4, tone: 'amazon' },
  { symbol: 'GOOG', name: 'Alphabet Inc.', quantity: 8, averagePrice: 150.25, currentPrice: 167.34, marketValue: 1_338.72, pnl: 136.72, pnlPercent: 11.38, weight: 1.0, tone: 'google' },
  { symbol: 'CASH', name: 'Cash', quantity: null, averagePrice: null, currentPrice: 1, marketValue: 12_430.18, pnl: null, pnlPercent: null, weight: 9.7, tone: 'cash' },
]

export const chartPoints: Record<string, string> = {
  '1D': '0,136 32,130 55,139 81,122 106,128 130,112 154,119 180,98 204,108 230,92 255,101 280,81 304,87 330,71 354,77 380,64 405,53 430,60 456,46 482,50 506,43 535,49 560,41 590,48 620,40 650,51 680,46',
  '1W': '0,145 32,148 55,133 81,138 106,119 130,126 154,111 180,116 204,98 230,105 255,83 280,91 304,74 330,79 354,68 380,74 405,52 430,62 456,47 482,51 506,40 535,52 560,45 590,53 620,47 650,55 680,50',
  '1M': '0,150 32,144 55,152 81,136 106,141 130,125 154,132 180,113 204,119 230,102 255,109 280,87 304,95 330,80 354,84 380,69 405,76 430,54 456,60 482,45 506,50 535,60 560,50 590,58 620,49 650,56 680,48',
  '3M': '0,150 18,153 36,148 54,152 72,146 90,150 108,144 126,142 144,146 162,139 180,141 198,135 216,138 234,132 252,129 270,134 288,128 306,131 324,121 342,125 360,108 378,113 396,104 414,109 432,103 450,107 468,114 486,105 504,111 522,99 540,104 558,93 576,100 594,86 612,94 630,72 648,80 666,58 680,46',
  '1Y': '0,150 32,146 55,153 81,145 106,150 130,139 154,146 180,126 204,135 230,111 255,118 280,99 304,105 330,87 354,94 380,73 405,81 430,58 456,66 482,46 506,52 535,40 560,47 590,37 620,45 650,53 680,49',
  YTD: '0,150 32,146 55,152 81,145 106,151 130,134 154,141 180,119 204,126 230,105 255,111 280,93 304,100 330,81 354,88 380,69 405,75 430,57 456,63 482,43 506,50 535,38 560,45 590,35 620,43 650,52 680,48',
  ALL: '0,151 32,154 55,148 81,153 106,145 130,150 154,139 180,131 204,140 230,118 255,124 280,105 304,112 330,93 354,99 380,80 405,87 430,66 456,73 482,53 506,59 535,42 560,48 590,40 620,47 650,56 680,54',
}

export type PerformancePoint = {
  x: number
  y: number
  date: string
  value: string
}

const chartRangeDays: Record<string, number> = { '1D': 1, '1W': 7, '1M': 30, '3M': 85, '1Y': 365, YTD: 145, ALL: 730 }
const chartStart = Date.UTC(2024, 1, 24)

function buildChartSeries(points: string, range: string): PerformancePoint[] {
  const coordinates = points.split(' ').map((point) => point.split(',').map(Number))
  const rangeDays = chartRangeDays[range] ?? 85
  return coordinates.map(([x, y], index) => {
    const elapsedDays = Math.round((index / Math.max(coordinates.length - 1, 1)) * rangeDays)
    const date = new Date(chartStart + elapsedDays * 86_400_000).toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })
    const value = 140 - ((y - 20) / 180) * 80
    return { x, y, date, value: `$${value.toFixed(2)}K` }
  })
}

export const chartSeries: Record<string, PerformancePoint[]> = Object.fromEntries(Object.entries(chartPoints).map(([range, points]) => [range, buildChartSeries(points, range)]))
