// ── Domain types ────────────────────────────────────────────────────────────

export interface Coordinates {
  latitude: number
  longitude: number
}

export interface AddressResult {
  normalized: string
  street: string
  number?: string
  neighborhood: string
  city: string
  state: string
  postal_code?: string
  coordinates: Coordinates
}

export type TrendValue = 'improving' | 'stable' | 'worsening' | 'insufficient_data'
export type DimensionStatus = 'available' | 'unavailable'
export type Grade = 'A+' | 'A' | 'B+' | 'B' | 'C+' | 'C' | 'D' | 'F'

export interface DimensionScore {
  score: number
  max: number
  trend: TrendValue | null
  status: DimensionStatus
}

export type DimensionKey =
  | 'security'
  | 'mobility'
  | 'infrastructure'
  | 'environment'
  | 'appreciation'
  | 'urban_context'

export interface AnalysisWarning {
  dimension: string
  provider: string
  message: string
}

export interface Score {
  composite: number
  max: number
  grade: Grade
  dimensions: Record<DimensionKey, DimensionScore>
}

export interface AnalysisResult {
  address: AddressResult
  score: Score
  risk_flags: string[]
  opportunity_flags: string[]
  insight: string | null
  insight_unavailable: boolean
  warnings: AnalysisWarning[]
  providers_used: string[]
  providers_unavailable?: string[]
  cached: boolean
  analyzed_at: string
}

// ── Error type ───────────────────────────────────────────────────────────────

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly body?: unknown,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

// ── Client ───────────────────────────────────────────────────────────────────

const BASE_URL: string = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? ''

/**
 * Sends a property analysis request to `POST /v1/property/analyze`.
 * Throws {@link ApiError} on non-200 responses.
 */
export async function analyzeProperty(
  address: string,
  options?: { forceRefresh?: boolean; apiKey?: string },
): Promise<AnalysisResult> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }

  if (options?.apiKey) {
    headers['X-Api-Key'] = options.apiKey
  }

  if (options?.forceRefresh) {
    headers['Cache-Control'] = 'no-cache'
  }

  const response = await fetch(`${BASE_URL}/v1/property/analyze`, {
    method: 'POST',
    headers,
    body: JSON.stringify({ address }),
  })

  if (!response.ok) {
    let body: unknown
    try {
      body = await response.json()
    } catch {
      body = undefined
    }

    const message =
      typeof body === 'object' && body !== null && 'message' in body
        ? String((body as { message: unknown }).message)
        : `HTTP ${response.status}: ${response.statusText}`

    throw new ApiError(response.status, message, body)
  }

  return response.json() as Promise<AnalysisResult>
}
