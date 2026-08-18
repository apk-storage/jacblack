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
  | 'localBlocked'
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


/**
 * Локальный ли адрес TorrServer.
 *
 * Публичный TorrServer мы дёргаем через свой бэкенд: страница отдаётся по
 * HTTPS, а TorrServer почти всегда по HTTP, и браузер режет такой запрос как
 * mixed content. Но у прокси есть обратная сторона — сервер в интернете
 * физически не видит домашнюю сеть, и запрос к 192.168.x.x он выполнить не
 * может. Мало того, приватные адреса ему запрещены нарочно: иначе jac.black
 * стал бы инструментом простукивания чужих локальных сетей.
 *
 * Поэтому локальные адреса идут из браузера напрямую — он-то в домашней сети
 * находится. Работает это не всегда: `127.0.0.1` браузеры считают доверённым
 * и пропускают даже со страницы по HTTPS, а вот `192.168.x.x` по HTTP со
 * страницы HTTPS блокируют. Такой случай мы честно называем в сообщении, а не
 * прячем за «сервер не ответил».
 */
export function isLocalTorrServer(baseUrl: string): boolean {
  let host = ''
  try {
    host = new URL(baseUrl.trim()).hostname
  } catch {
    return false
  }
  if (host === 'localhost' || host.endsWith('.local')) return true
  const части = host.split('.').map(Number)
  if (части.length !== 4 || части.some((ч) => Number.isNaN(ч))) return false
  const [а, б] = части
  return (
    а === 127 ||
    а === 10 ||
    (а === 192 && б === 168) ||
    (а === 172 && б >= 16 && б <= 31) ||
    (а === 169 && б === 254)
  )
}

/** Отправка прямо из браузера — для TorrServer в домашней сети. */
async function sendDirect(magnet: string, creds: TorrServerCredentials): Promise<void> {
  const origin = creds.baseUrl.trim().replace(/\/+$/, '')
  const заголовки: Record<string, string> = { 'Content-Type': 'application/json' }
  if (creds.login) {
    заголовки.Authorization = 'Basic ' + btoa(`${creds.login}:${creds.password ?? ''}`)
  }

  let res: Response
  try {
    res = await fetch(`${origin}/torrents`, {
      method: 'POST',
      headers: заголовки,
      body: JSON.stringify({ action: 'add', link: magnet, save_to_db: true }),
    })
  } catch {
    // Сюда попадает и блокировка mixed content, и недоступность сервера —
    // различить их из JavaScript нельзя, поэтому код общий, а текст ошибки
    // объясняет оба случая.
    throw new TorrServerError('localBlocked')
  }

  if (res.status === 401) throw new TorrServerError('unauthorized', 401)
  if (!res.ok) throw new TorrServerError('request', res.status)
}

/** POST magnet to a TorrServer `/torrents` endpoint (Basic auth supported via URL or creds). */
export async function sendToTorrServer(
  magnet: string,
  creds: TorrServerCredentials,
): Promise<void> {
  if (!isSafeMagnetUrl(magnet)) throw new TorrServerError('invalidMagnet')
  const baseUrl = creds.baseUrl.trim()
  if (!baseUrl) throw new TorrServerError('missingUrl')

  // Домашний TorrServer через наш сервер недостижим: сервер в интернете,
  // а TorrServer — в локальной сети человека. Такие адреса шлём из браузера.
  if (isLocalTorrServer(baseUrl)) return sendDirect(magnet, creds)

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
