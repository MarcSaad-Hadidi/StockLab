import type { StockNews } from '../api/marketDataApi'

/** Keep headlines about the selected company, not articles with a passing ticker mention. */
export function companyHeadlines(news: StockNews | undefined, symbol: string, companyName: string) {
  const normalize = (value: string) => value.normalize('NFKD').replace(/\p{M}/gu, '').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim()
  const ticker = normalize(symbol.split(':')[0])
  const stem = normalize(companyName).replace(/^the /, '').split(/\b(?:incorporated|inc|corporation|corp|company|co|limited|ltd|plc|holdings|holding|class|common)\b/)[0].trim()
  const leading = stem.split(' ')[0]
  const generic = new Set(['the', 'bank', 'group', 'global', 'first', 'united', 'national', 'american'])
  // Short tickers such as A or ON are common words, not reliable headline matches.
  const identities = [ticker.length >= 3 ? ticker : '', stem, leading.length >= 4 && !generic.has(leading) ? leading : ''].filter(Boolean)
  return news?.articles.filter(article => {
    const headline = ` ${normalize(article.title)} `
    return identities.some(identity => headline.includes(` ${identity} `))
  }) ?? []
}
