import { createMarketDataApi } from './marketDataApi'
export const marketDataApi = createMarketDataApi(
  import.meta.env?.VITE_STOCKLAB_API_BASE_URL || ''
)
