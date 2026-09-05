import type { ReactNode, SVGProps } from 'react'

export type MarketIconName =
  | 'arrowLeft'
  | 'bell'
  | 'briefcase'
  | 'chart'
  | 'check'
  | 'chevronDown'
  | 'chevronRight'
  | 'close'
  | 'expand'
  | 'filter'
  | 'globe'
  | 'grid'
  | 'info'
  | 'list'
  | 'mail'
  | 'menu'
  | 'plus'
  | 'robot'
  | 'search'
  | 'star'
  | 'sparkles'
  | 'user'
  | 'wallet'
  | 'logout'

type MarketIconProps = SVGProps<SVGSVGElement> & {
  name: MarketIconName
  size?: number
  filled?: boolean
}

const iconPaths: Record<MarketIconName, ReactNode> = {
  arrowLeft: <path d="m14.5 5-7 7 7 7M8 12h12" />,
  bell: (
    <>
      <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" />
      <path d="M10 21h4" />
    </>
  ),
  briefcase: (
    <>
      <rect x="3" y="7" width="18" height="13" rx="2" />
      <path d="M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M3 12h18M9 12v2h6v-2" />
    </>
  ),
  chart: (
    <>
      <path d="M4 19V5M4 19h17" />
      <path d="m7 15 3-4 3 2 5-7" />
      <path d="M16 6h2v2" />
    </>
  ),
  check: <path d="m5 12 4 4L19 6" />,
  chevronDown: <path d="m6 9 6 6 6-6" />,
  chevronRight: <path d="m9 6 6 6-6 6" />,
  close: <path d="m6 6 12 12M18 6 6 18" />,
  expand: <path d="M8 4H4v4M16 4h4v4M20 16v4h-4M4 16v4h4M4 4l5 5M20 4l-5 5M20 20l-5-5M4 20l5-5" />,
  filter: (
    <>
      <path d="M4 6h16M7 12h10M10 18h4" />
      <circle cx="8" cy="6" r="1.5" fill="currentColor" stroke="none" />
      <circle cx="15" cy="12" r="1.5" fill="currentColor" stroke="none" />
      <circle cx="12" cy="18" r="1.5" fill="currentColor" stroke="none" />
    </>
  ),
  globe: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M3 12h18M12 3c2.2 2.4 3.2 5.4 3.2 9s-1 6.6-3.2 9c-2.2-2.4-3.2-5.4-3.2-9S9.8 5.4 12 3Z" />
    </>
  ),
  grid: (
    <>
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </>
  ),
  info: (
    <>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 11v5M12 8h.01" />
    </>
  ),
  list: (
    <>
      <path d="M8 6h13M8 12h13M8 18h13" />
      <path d="M3.5 6h.01M3.5 12h.01M3.5 18h.01" />
    </>
  ),
  mail: (
    <>
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="m4 7 8 6 8-6" />
    </>
  ),
  menu: <path d="M4 7h16M4 12h16M4 17h16" />,
  plus: <path d="M12 5v14M5 12h14" />,
  robot: (
    <>
      <rect x="4" y="6" width="16" height="13" rx="3" />
      <path d="M12 3v3M8 12h.01M16 12h.01M8 16h8" />
    </>
  ),
  search: (
    <>
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 4.5 4.5" />
    </>
  ),
  star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9L12 3Z" />,
  sparkles: (
    <>
      <path d="m12 3-1.4 5.6L5 10l5.6 1.4L12 17l1.4-5.6L19 10l-5.6-1.4L12 3Z" />
      <path d="m19 16-.7 2.3L16 19l2.3.7L19 22l.7-2.3L22 19l-2.3-.7L19 16Z" />
    </>
  ),
  user: (
    <>
      <circle cx="12" cy="8" r="3.5" />
      <path d="M5 20a7 7 0 0 1 14 0" />
    </>
  ),
  wallet: (
    <>
      <path d="M4 7.5A2.5 2.5 0 0 1 6.5 5H19a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H6.5A2.5 2.5 0 0 1 4 16.5v-9Z" />
      <path d="M4 8h14a3 3 0 0 1 3 3v2h-5a2 2 0 0 1 0-4h5M16 11h.01" />
    </>
  ),
  logout: (
    <>
      <path d="M10 17l5-5-5-5M15 12H3" />
      <path d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4" />
    </>
  ),
}

export function MarketIcon({ name, size = 16, filled = false, ...props }: MarketIconProps) {
  if (name === 'star') {
    return (
      <svg
        aria-hidden="true"
        fill={filled ? 'currentColor' : 'none'}
        height={size}
        viewBox="0 0 24 24"
        width={size}
        stroke="currentColor"
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeWidth="1.7"
        {...props}
      >
        {iconPaths[name]}
      </svg>
    )
  }

  return (
    <svg
      aria-hidden="true"
      fill="none"
      height={size}
      viewBox="0 0 24 24"
      width={size}
      stroke="currentColor"
      strokeLinecap="round"
      strokeLinejoin="round"
      strokeWidth="1.7"
      {...props}
    >
      {iconPaths[name]}
    </svg>
  )
}
