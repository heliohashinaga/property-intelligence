const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''

export interface Coordinates {
  latitude: number
  longitude: number
}

export interface AddressResult {
  normalized: string
  street?: string
  number?: string
  neighborhood?: string
  city?: string
  state?: string
  postal_code?: string
  coordinates: Coordinates
}

export type TrendDirection = 'improving' | 'stable' | 'worsening' | null
export type DimensionStatus = 'available' | 'unavailable'

export interface DimensionScore {
  score: number | null
  max: number
  trend: TrendDirection
  status: DimensionStatus
}

export interface ScoreDimensions {
  security: DimensionScore
  mobility: DimensionScore
  infrastructure: DimensionScore
  environment: DimensionScore
  appreciation: DimensionScore
  urban_context: DimensionScore
}

export interface Score {
  composite: number
  max: number
  grade: string
  dimensions: ScoreDimensions
}

export interface AnalysisWarning {
  dimension: string
  provider: string
  message: string
}

export interface PropertyAnalysisResponse {
  address: AddressResult
  score: Score
  risk_flags: string[]
  opportunity_flags: string[]
  insight: string | null
  insight_unavailable: boolean
  warnings: AnalysisWarning[]
  providers_used: string[]
  providers_unavailable: string[]
  cached: boolean
  analysis_id: string
  analyzed_at: string
}

export interface ApiError {
  error: string
  message: string
  field?: string
  candidates?: string[]
  providers_unavailable?: string[]
}

export class PropertyApiError extends Error {
  readonly status: number
  readonly body: ApiError

  constructor(status: number, body: ApiError) {
    super(body.message)
    this.name = 'PropertyApiError'
    this.status = status
    this.body = body
  }
}

export async function analyzeProperty(
  address: string,
  apiKey?: string,
): Promise<PropertyAnalysisResponse> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (apiKey) {
    headers['X-Api-Key'] = apiKey
  }

  const response = await fetch(`${API_BASE_URL}/v1/property/analyze`, {
    method: 'POST',
    headers,
    body: JSON.stringify({ address }),
  })

  if (!response.ok) {
    const body: ApiError = await response.json().catch(() => ({
      error: 'unknown_error',
      message: 'Erro desconhecido ao processar a requisição.',
    }))
    throw new PropertyApiError(response.status, body)
  }

  return response.json() as Promise<PropertyAnalysisResponse>
}
