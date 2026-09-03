type StockLogoProps = {
  symbol: string
  size?: 'small' | 'large'
}

function logoClass(symbol: string) {
  return symbol.toLowerCase().replace('.', '-')
}

export function StockLogo({ symbol, size = 'small' }: StockLogoProps) {
  if (symbol === 'MSFT') {
    return (
      <span aria-hidden="true" className={`market-stock-logo market-stock-logo-msft market-stock-logo-${size}`}>
        <i />
        <i />
        <i />
        <i />
      </span>
    )
  }

  const letter = symbol === 'GOOGL' ? 'G' : symbol === 'AMZN' ? 'a' : symbol === 'BTC' ? '₿' : symbol.charAt(0)

  return (
    <span aria-hidden="true" className={`market-stock-logo market-stock-logo-${logoClass(symbol)} market-stock-logo-${size}`}>
      {letter}
    </span>
  )
}
