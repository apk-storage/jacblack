const MAX_MAGNET_LENGTH = 8_192

/** Rejects non-magnet schemes and oversized payloads before copy / TorrServer send. */
export function isSafeMagnetUrl(value: string | null | undefined): boolean {
  if (!value || typeof value !== 'string') return false
  const magnet = value.trim()
  if (!magnet || magnet.length > MAX_MAGNET_LENGTH) return false
  return /^magnet:\?/i.test(magnet)
}

/** Extracts a lowercase info-hash from a magnet URI, or `''` if invalid. */
export function extractInfoHash(magnet: string | null | undefined): string {
  if (!isSafeMagnetUrl(magnet)) return ''
  const m = (magnet as string).match(
    /urn:btih:([a-fA-F0-9]{40}|[a-zA-Z2-7]{32}|[a-fA-F0-9]{64})/i,
  )
  return m ? m[1].toLowerCase() : ''
}

/** Clipboard write with a `document.execCommand` fallback for older WebViews. */
export async function copyText(text: string): Promise<void> {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text)
    return
  }
  const ta = document.createElement('textarea')
  ta.value = text
  ta.style.cssText = 'position:fixed;opacity:0;top:0;left:0'
  document.body.appendChild(ta)
  ta.focus()
  ta.select()
  try {
    const ok = document.execCommand('copy')
    if (!ok) throw new Error('copy failed')
  } finally {
    document.body.removeChild(ta)
  }
}

export type TorrServerCredentials = {
  baseUrl: string
  login?: string
  password?: string
}

export type TorrServerErrorCode =
  | 'invalidMagnet'
  | 'missingUrl'
  | 'unauthorized'
  | 'cors'
  | 'request'

export class TorrServerError extends Error {
  readonly code: TorrServerErrorCode
  readonly status?: number

  constructor(
    code: TorrServerErrorCode,
    status?: number,
  ) {
    super(code)
    this.name = 'TorrServerError'
    this.code = code
    this.status = status
  }
}

/** POST magnet to a TorrServer `/torrents` endpoint (Basic auth supported via URL or creds). */
export async function sendToTorrServer(
  magnet: string,
  creds: TorrServerCredentials,
): Promise<void> {
  if (!isSafeMagnetUrl(magnet)) throw new TorrServerError('invalidMagnet')
  const baseUrl = creds.baseUrl.trim()
  if (!baseUrl) throw new TorrServerError('missingUrl')

  // Отправляем через бэкенд jac.black (same-origin, HTTPS), а не напрямую в
  // TorrServer. Прямой запрос из браузера в HTTP-TorrServer блокируется как
  // mixed content ещё до отправки — сервер жив, а «не ответил». Сервер такой
  // проблемы не имеет: awg → TorrServer идёт сервер-к-серверу.
  const res = await fetch('/torrserver/add', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      baseUrl,
      login: creds.login ?? '',
      password: creds.password ?? '',
      magnet,
    }),
  }).catch(() => null)

  if (!res) throw new TorrServerError('request')

  const data = (await res.json().catch(() => null)) as
    | { ok?: boolean; code?: TorrServerErrorCode; status?: number }
    | null

  if (data?.ok) return
  throw new TorrServerError(data?.code ?? 'request', data?.status)
}
