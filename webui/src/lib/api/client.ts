import { getApiKey, getDevKey } from '@/lib/storage'
import type { paths } from '@/lib/api/types'

export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

export type ApiClientOptions = {
  /** Override base URL (default: same origin) */
  baseUrl?: string
  /** Request timeout in ms */
  timeoutMs?: number
  /** Include X-Api-Key from storage when set */
  withApiKey?: boolean
  /** Include X-Dev-Key from storage when set */
  withDevKey?: boolean
  /** Abort request from the caller */
  signal?: AbortSignal
}

type HttpMethod = 'get' | 'post' | 'put' | 'patch' | 'delete'

type PathKey = keyof paths
type GetJson<Path extends PathKey> = paths[Path] extends {
  get: {
    responses: {
      200: { content: { 'application/json': infer Response } }
    }
  }
}
  ? Response
  : never

const DEFAULT_TIMEOUT_MS = 30_000

function buildUrl(
  baseUrl: string,
  path: string,
  query?: Record<string, string | number | boolean | undefined | null>,
): string {
  const url = new URL(path, baseUrl || window.location.origin)
  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value === undefined || value === null || value === '') continue
      url.searchParams.set(key, String(value))
    }
  }
  return url.toString()
}

async function parseBody(res: Response): Promise<unknown> {
  const text = await res.text()
  if (!text) return undefined
  const ct = res.headers.get('content-type') ?? ''
  if (ct.includes('application/json')) {
    try {
      return JSON.parse(text) as unknown
    } catch {
      return text
    }
  }
  return text
}

/**
 * Typed fetch wrapper: timeout, optional API/Dev keys, JSON parse, and `ApiError` on failure.
 */
export async function apiRequest<T = unknown>(
  path: string,
  init: RequestInit & {
    query?: Record<string, string | number | boolean | undefined | null>
  } = {},
  options: ApiClientOptions = {},
): Promise<T> {
  const {
    baseUrl = '',
    timeoutMs = DEFAULT_TIMEOUT_MS,
    withApiKey = true,
    withDevKey = false,
    signal: optionSignal,
  } = options

  const headers = new Headers(init.headers)
  if (!headers.has('Accept')) headers.set('Accept', 'application/json')

  if (withApiKey) {
    const key = getApiKey()
    if (key) headers.set('X-Api-Key', key)
  }
  if (withDevKey) {
    const key = getDevKey()
    if (key) headers.set('X-Dev-Key', key)
  }

  const { query, ...rest } = init
  const controller = new AbortController()
  const externalSignal = init.signal ?? optionSignal
  const abortFromCaller = () => controller.abort(externalSignal?.reason)
  if (externalSignal?.aborted) abortFromCaller()
  else externalSignal?.addEventListener('abort', abortFromCaller, { once: true })
  const timer = window.setTimeout(() => controller.abort(), timeoutMs)

  try {
    const res = await fetch(buildUrl(baseUrl, path, query), {
      ...rest,
      headers,
      signal: controller.signal,
    })

    const body = await parseBody(res)

    if (!res.ok) {
      const message =
        typeof body === 'object' &&
        body !== null &&
        'message' in body &&
        typeof (body as { message: unknown }).message === 'string'
          ? (body as { message: string }).message
          : `HTTP ${res.status}`
      throw new ApiError(res.status, message, body)
    }

    return body as T
  } finally {
    window.clearTimeout(timer)
    externalSignal?.removeEventListener('abort', abortFromCaller)
  }
}

/** Typed helpers for JacRed HTTP endpoints used by the SPA. */
export const apiClient = {
  getHealth(options?: ApiClientOptions) {
    return apiRequest<GetJson<'/health'> | string>(
      '/health',
      { method: 'GET' },
      { ...options, withApiKey: false, withDevKey: false },
    )
  },

  getConf(options?: ApiClientOptions) {
    return apiRequest<GetJson<'/api/v1.0/conf'>>(
      '/api/v1.0/conf',
      { method: 'GET' },
      options,
    )
  },

  getTorrents(
    query: Record<string, string | number | boolean | undefined | null>,
    options?: ApiClientOptions,
  ) {
    return apiRequest<GetJson<'/api/v1.0/torrents'>>(
      '/api/v1.0/torrents',
      { method: 'GET', query },
      options,
    )
  },

  getStatsTorrents(options?: ApiClientOptions) {
    return apiRequest<GetJson<'/stats/torrents'>>('/stats/torrents', { method: 'GET' }, {
      ...options,
      timeoutMs: options?.timeoutMs ?? 10_000,
    })
  },

  getStatsMeta(options?: ApiClientOptions) {
    return apiRequest<GetJson<'/stats/meta'>>('/stats/meta', { method: 'GET' }, {
      ...options,
      timeoutMs: options?.timeoutMs ?? 5_000,
    })
  },

  /**
   * Чему в выдаче можно верить: откуда берутся живые числа раздающих,
   * сколько раздач опознано удалёнными, насколько полон словарь кодов.
   */
  getStatsQuality(options?: ApiClientOptions) {
    return apiRequest<unknown>('/stats/quality', { method: 'GET' }, {
      ...options,
      timeoutMs: options?.timeoutMs ?? 5_000,
    })
  },

  /** Свежесть источников: давно ли каждый из них выкладывал что-то новое. */
  getStatsTrackers(options?: ApiClientOptions) {
    return apiRequest<unknown>('/stats/trackers', { method: 'GET' }, {
      ...options,
      timeoutMs: options?.timeoutMs ?? 5_000,
    })
  },

  /** Ход глубоких обходов: что идёт сейчас и сколько осталось в очередях. */
  getStatsCrawl(options?: ApiClientOptions) {
    return apiRequest<unknown>('/stats/crawl', { method: 'GET' }, {
      ...options,
      timeoutMs: options?.timeoutMs ?? 5_000,
    })
  },
}

export type { PathKey, HttpMethod, paths }
