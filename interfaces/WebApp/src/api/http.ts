const configuredBase = import.meta.env.VITE_API_BASE as string | undefined

export const apiBaseUrl = (configuredBase?.trim() || 'http://localhost:5080').replace(/\/$/, '')

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly traceId?: string
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

export async function apiRequest<T>(
  path: string,
  options: RequestInit = {},
  signal?: AbortSignal
): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...options,
    signal,
    headers: {
      Accept: 'application/json',
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...options.headers
    }
  })

  if (!response.ok) {
    const problem = await readProblemDetails(response)
    throw new ApiError(problem.message, response.status, problem.traceId)
  }

  return (await response.json()) as T
}

async function readProblemDetails(response: Response): Promise<{ message: string; traceId?: string }> {
  const fallback = `API request failed with HTTP ${response.status}`
  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('application/json')) {
    return { message: fallback }
  }

  const payload = (await response.json()) as { detail?: string; title?: string; traceId?: string }
  return {
    message: payload.detail ?? payload.title ?? fallback,
    traceId: payload.traceId
  }
}
