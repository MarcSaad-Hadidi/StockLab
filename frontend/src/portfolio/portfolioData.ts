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
  '1D': '0,136 32,130 55,139 81,122 106,128 130,112 154,119 180,98 204,108 230,92 255,101 280,81 304,87 330,71 354,77 380,64 405,53 430,60 456,46 482,50 506,36 535,43 560,28 590,35 620,21 650,25 680,12',
  '1W': '0,145 32,148 55,133 81,138 106,119 130,126 154,111 180,116 204,98 230,105 255,83 280,91 304,74 330,79 354,68 380,74 405,52 430,62 456,47 482,51 506,40 535,29 560,35 590,24 620,28 650,16 680,22',
  '1M': '0,150 32,144 55,152 81,136 106,141 130,125 154,132 180,113 204,119 230,102 255,109 280,87 304,95 330,80 354,84 380,69 405,76 430,54 456,60 482,45 506,50 535,36 560,43 590,28 620,37 650,20 680,27',
  '3M': '0,158 18,153 36,157 54,148 72,151 90,146 108,153 126,148 144,126 162,130 180,116 198,123 216,113 234,126 252,119 270,132 288,106 306,113 324,91 342,101 360,88 378,96 396,82 414,79 432,87 450,80 468,89 486,76 504,71 522,77 540,53 558,60 576,47 594,53 612,44 630,32 648,37 666,48 680,44',
  '1Y': '0,174 32,164 55,172 81,151 106,159 130,142 154,149 180,126 204,135 230,111 255,118 280,99 304,105 330,87 354,94 380,73 405,81 430,58 456,66 482,46 506,52 535,35 560,42 590,26 620,34 650,17 680,23',
  YTD: '0,165 32,160 55,168 81,148 106,154 130,134 154,141 180,119 204,126 230,105 255,111 280,93 304,100 330,81 354,88 380,69 405,75 430,57 456,63 482,43 506,50 535,32 560,39 590,25 620,31 650,16 680,22',
  ALL: '0,180 32,174 55,181 81,160 106,166 130,149 154,155 180,131 204,140 230,118 255,124 280,105 304,112 330,93 354,99 380,80 405,87 430,66 456,73 482,53 506,59 535,42 560,48 590,32 620,40 650,23 680,30',
}
