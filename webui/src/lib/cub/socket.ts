/**
 * Контроллер сокета CUB — «пульт» Лампы для функции «В Лампе».
 *
 * Протокол восстановлен реверсом app.min.js Лампы (2026-08-06). Полный разбор
 * и обоснование — в personal-hub/lampac/JACBLACK-V-LAMPE.md. Кратко:
 *
 * - Лампа держит WebSocket к `wss://<зеркало>:8443` (зеркала перебираются при
 *   обрыве). Устройства одного аккаунта CUB видят команды друг друга — сервер
 *   роутит по полю `account` в каждом сообщении.
 * - Отправка: JSON с полями device_id/name/method/version/account/premium/terminal.
 *   Пинг — строкой `'ping'`, ответ — `'pong'`.
 * - Приём: JSON, ветвление по `result.method`. Нам приходят `devices` (список
 *   устройств аккаунта) и `terminal_result` (ответ на eval).
 *
 * Запуск раздачи идёт через `terminal_eval`: на выбранное устройство шлётся JS,
 * который открывает карточку (imdb→tmdb) и добавляет magnet в его локальный
 * TorrServer. Требует, чтобы на устройстве был задан код терминала
 * (`terminal_access`) и тот же код введён здесь.
 *
 * Это обёртка вокруг СТОРОННЕГО недокументированного протокола cub.rip — он
 * может измениться. Поэтому список зеркал и порт вынесены в константы, а формат
 * сообщения собран точно по реверсу.
 */

/** Зеркала сокета CUB (soc_mirrors из app.min.js). Перебор при обрыве. */
const SOCKET_MIRRORS = ['cub.rip', 'kurwa-bober.ninja', 'nackhui.com']
const SOCKET_PORT = 8443
const PING_INTERVAL_MS = 20_000

/** Устройство аккаунта CUB, как приходит в method:'devices'. */
export type CubDevice = {
  uid: string
  name: string
  [key: string]: unknown
}

/** Аккаунт CUB — то, что Лампа кладёт в Storage под 'account' после входа. */
export type CubAccount = Record<string, unknown>

export type CubSocketState = 'idle' | 'connecting' | 'open' | 'closed'

type Handlers = {
  onState?: (state: CubSocketState) => void
  onDevices?: (devices: CubDevice[]) => void
  onTerminalResult?: (result: unknown) => void
  /**
   * Любое пришедшее сообщение — для видимых техданных в диалоге.
   *
   * Без него пустой список устройств неотличим от «сервер вообще молчит», а
   * лезть в консоль браузера с телефона неудобно.
   */
  onAny?: (method: string, size: number) => void
}

/**
 * Свой идентификатор устройства-пульта. Лампа генерирует uid один раз и хранит;
 * повторяем поведение, чтобы сервер видел стабильный источник команд.
 */
function selfDeviceId(): string {
  const KEY = 'jb_cub_uid'
  try {
    let uid = localStorage.getItem(KEY)
    if (!uid) {
      uid = 'jb-' + Math.random().toString(36).slice(2) + Date.now().toString(36)
      localStorage.setItem(KEY, uid)
    }
    return uid
  } catch {
    return 'jb-ephemeral'
  }
}

export class CubSocket {
  private ws: WebSocket | null = null
  private mirror = 0
  private ping: ReturnType<typeof setInterval> | null = null
  private closedByUs = false
  private readonly account: CubAccount
  private readonly handlers: Handlers
  private readonly uid = selfDeviceId()

  constructor(account: CubAccount, handlers: Handlers = {}) {
    this.account = account
    this.handlers = handlers
  }

  get connected(): boolean {
    return this.ws?.readyState === WebSocket.OPEN
  }

  connect(): void {
    this.closedByUs = false
    this.open()
  }

  close(): void {
    this.closedByUs = true
    this.stopPing()
    try { this.ws?.close() } catch { /* ignore */ }
    this.ws = null
    this.handlers.onState?.('closed')
  }

  /** Запросить список устройств аккаунта. Лампа шлёт это раз в 3 с при выборе. */
  requestDevices(): void {
    this.send('devices', {})
  }

  /**
   * Активировать терминал на устройстве присланным кодом. Должен совпасть с
   * тем, что пользователь задал на ТВ (Storage 'terminal_access'). Без успешной
   * активации eval не выполнится.
   */
  terminalActivate(code: string): void {
    this.send('terminal_activate', { code })
  }

  /**
   * Выполнить JS на устройстве. code — тот же терминал-код; eval — тело.
   * Устройство сверяет code и делает `eval(eval)`, ответ придёт в
   * terminal_result.
   */
  terminalEval(code: string, js: string): void {
    this.send('terminal_eval', { code, eval: js })
  }

  // ── внутреннее ──────────────────────────────────────────────────────────

  private open(): void {
    const host = SOCKET_MIRRORS[this.mirror % SOCKET_MIRRORS.length]
    this.mirror++
    const url = `wss://${host}:${SOCKET_PORT}`

    this.handlers.onState?.('connecting')
    try {
      this.ws = new WebSocket(url)
    } catch {
      this.scheduleReconnect()
      return
    }

    this.ws.addEventListener('open', () => {
      this.handlers.onState?.('open')
      this.startPing()
      this.requestDevices()
    })

    this.ws.addEventListener('message', (ev) => this.onMessage(ev))

    this.ws.addEventListener('close', () => {
      this.stopPing()
      if (!this.closedByUs) this.scheduleReconnect()
    })

    this.ws.addEventListener('error', () => {
      try { this.ws?.close() } catch { /* ignore */ }
    })
  }

  private onMessage(ev: MessageEvent): void {
    // Пинг-ответ — не JSON.
    if (ev.data === 'pong') return

    let result: { method?: string; data?: unknown }
    try {
      result = JSON.parse(ev.data as string)
    } catch {
      return
    }

    this.handlers.onAny?.(
      String(result.method || '—'),
      Array.isArray(result.data) ? result.data.length : 0,
    )

    if (result.method === 'devices') {
      const list = Array.isArray(result.data) ? (result.data as CubDevice[]) : []
      this.handlers.onDevices?.(list)
    } else if (result.method === 'terminal_result') {
      this.handlers.onTerminalResult?.(result.data)
    }
  }

  /**
   * Сборка и отправка сообщения ровно по формату Лампы (функция send в
   * app.min.js): к данным добавляются обязательные метаполя. account — то, чем
   * сервер роутит команду к устройствам того же пользователя.
   */
  private send(method: string, data: Record<string, unknown>): void {
    if (this.ws?.readyState !== WebSocket.OPEN) return
    const payload = {
      ...data,
      device_id: this.uid,
      name: 'JacBlack - web',
      method,
      version: 1,
      account: this.account,
      premium: false,
      terminal: '',
    }
    try {
      this.ws.send(JSON.stringify(payload))
    } catch {
      /* соединение оборвалось — переподключение поднимет заново */
    }
  }

  private startPing(): void {
    this.stopPing()
    this.ping = setInterval(() => {
      if (this.ws?.readyState === WebSocket.OPEN) {
        try { this.ws.send('ping') } catch { /* ignore */ }
      }
    }, PING_INTERVAL_MS)
  }

  private stopPing(): void {
    if (this.ping) { clearInterval(this.ping); this.ping = null }
    this.ping = null
  }

  private scheduleReconnect(): void {
    this.handlers.onState?.('closed')
    // Небольшая пауза и следующее зеркало — как перебор soc_mirrors у Лампы.
    setTimeout(() => { if (!this.closedByUs) this.open() }, 2000)
  }
}
