// Small inline icons (SF Symbols–style strokes) so the app has no icon dependency.

type IconProps = { size?: number }

const stroke = {
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.8,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
}

export const ArrowRight = ({ size = 14 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 16 16" aria-hidden="true">
    <path d="M3 8h10m-4-4 4 4-4 4" {...stroke} />
  </svg>
)

export const Swap = ({ size = 16 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 16 16" aria-hidden="true">
    <path d="M2.5 5.5h10m-3-3 3 3-3 3M13.5 10.5h-10m3-3-3 3 3 3" {...stroke} />
  </svg>
)

export const Plus = ({ size = 14 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 16 16" aria-hidden="true">
    <path d="M8 3v10M3 8h10" {...stroke} strokeWidth={2} />
  </svg>
)

export const Minus = ({ size = 14 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 16 16" aria-hidden="true">
    <path d="M3 8h10" {...stroke} strokeWidth={2} />
  </svg>
)

export const Close = ({ size = 12 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 16 16" aria-hidden="true">
    <path d="m4 4 8 8m0-8-8 8" {...stroke} strokeWidth={2} />
  </svg>
)

export const Warning = ({ size = 18 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 20 20" aria-hidden="true">
    <path d="M10 2.8 18 17H2L10 2.8Z" fill="currentColor" opacity="0.18" />
    <path d="M10 2.8 18 17H2L10 2.8Z" {...stroke} />
    <path d="M10 8v4" {...stroke} strokeWidth={2} />
    <circle cx="10" cy="14.6" r="1" fill="currentColor" />
  </svg>
)

export const Check = ({ size = 18 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 20 20" aria-hidden="true">
    <circle cx="10" cy="10" r="8.5" fill="currentColor" />
    <path d="m6.2 10.3 2.5 2.5 5-5.2" fill="none" stroke="#fff" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" />
  </svg>
)

export const Info = ({ size = 18 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 20 20" aria-hidden="true">
    <circle cx="10" cy="10" r="8.5" fill="currentColor" />
    <path d="M10 9v5" fill="none" stroke="#fff" strokeWidth={2} strokeLinecap="round" />
    <circle cx="10" cy="6.3" r="1.1" fill="#fff" />
  </svg>
)

export const Refresh =({ size = 16 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 16 16" aria-hidden="true">
    <path d="M13.5 8a5.5 5.5 0 1 1-1.6-3.9M13.5 2.5v3h-3" {...stroke} />
  </svg>
)

export const Boxes = ({ size = 40 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 40 40" aria-hidden="true">
    <path d="M6 14 20 7l14 7v13l-14 7-14-7V14Z" {...stroke} strokeWidth={1.6} />
    <path d="m6 14 14 7 14-7M20 21v13" {...stroke} strokeWidth={1.6} />
  </svg>
)

export const TransferGlyph = ({ size = 18 }: IconProps) => (
  <svg width={size} height={size} viewBox="0 0 20 20" aria-hidden="true">
    <path d="M4 7h11m-3.5-3.5L15 7l-3.5 3.5M16 13H5m3.5-3.5L5 13l3.5 3.5" {...stroke} strokeWidth={2} />
  </svg>
)
