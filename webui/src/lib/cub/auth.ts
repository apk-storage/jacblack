/**
 * Вход в аккаунт CUB для функции «В Лампе».
 *
 * Реверснуто из app.min.js (2026-08-06): Лампа привязывает устройство к аккаунту
 * так — `POST https://<cub-домен>/api/device/add` с телом `{ code: <пинкод> }`,
 * ответ становится объектом `account` (в нём email и токены), Лампа кладёт его
 * в Storage под 'account'. Именно `account` затем идёт в каждом сообщении сокета,
 * и по нему сервер CUB роутит команды между устройствами одного пользователя.
 *
 * Флоу для jac.black:
 *   1. Пользователь на `https://cub.rip/add` (в своей учётке) получает КОД.
 *   2. Вводит код здесь.
 *   3. Мы шлём device/add {code} → получаем account, храним локально.
 *   4. С этим account коннектимся к сокету (CubSocket) и видим свои устройства.
 *
 * CORS: браузерный POST с jac.black на cub.rip — cross-origin. cub.rip может его
 * не разрешить. Поэтому по умолчанию идём через СВОЙ бэкенд-прокси
 * (`/cub/device-add`), а прямой запрос оставлен как запасной. Прокси на стороне
 * JacBlack ещё нужно поднять (аналогично проксированию TorrServer).
 */

const CUB_API = 'https://cub.rip/api'
const ACCOUNT_KEY = 'jb_cub_account'

export type CubAccount = Record<string, unknown>

/** Сохранённый аккаунт CUB, если пользователь уже входил. */
export function loadAccount(): CubAccount | null {
  try {
    const raw = localStorage.getItem(ACCOUNT_KEY)
    return raw ? (JSON.parse(raw) as CubAccount) : null
  } catch {
    return null
  }
}

export function saveAccount(account: CubAccount): void {
  try {
    localStorage.setItem(ACCOUNT_KEY, JSON.stringify(account))
  } catch {
    /* приватный режим — аккаунт проживёт только эту сессию */
  }
}

export function clearAccount(): void {
  try {
    localStorage.removeItem(ACCOUNT_KEY)
  } catch {
    /* ignore */
  }
}

/**
 * Обменять код добавления устройства на аккаунт CUB.
 *
 * Сначала пробуем свой прокси (обходит CORS), при его отсутствии — прямой
 * запрос на cub.rip. `code` — то, что пользователь получил на cub.rip/add.
 */
export async function loginWithCode(code: string, signal?: AbortSignal): Promise<CubAccount> {
  const trimmed = String(code).trim()
  if (!/^\d+$/.test(trimmed)) {
    throw new Error('Код должен быть числом — тем, что показан на cub.rip/add')
  }

  const account = await deviceAdd(trimmed, signal)
  if (!account || typeof account !== 'object') {
    throw new Error('CUB не вернул аккаунт — проверьте код (он одноразовый и быстро истекает)')
  }
  saveAccount(account)
  return account
}

async function deviceAdd(code: string, signal?: AbortSignal): Promise<CubAccount> {
  // 1) через свой бэкенд-прокси — без CORS-проблем.
  try {
    const viaProxy = await postJson('/cub/device-add', { code }, signal)
    if (viaProxy) return viaProxy
  } catch (e) {
    // прокси может быть ещё не поднят — падаем в прямой запрос
    if ((e as { name?: string }).name === 'AbortError') throw e
  }

  // 2) прямой запрос на cub.rip (сработает, только если CORS открыт).
  const direct = await postJson(`${CUB_API}/device/add`, { code }, signal)
  return direct as CubAccount
}

async function postJson(url: string, body: unknown, signal?: AbortSignal): Promise<CubAccount> {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal,
  })
  if (!res.ok) {
    throw new Error(`CUB device/add вернул ${res.status}`)
  }
  return (await res.json()) as CubAccount
}
