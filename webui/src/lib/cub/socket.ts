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

/** Как часто переспрашивать список устройств. У Лампы в окне трансляции 3 секунды. */
const DEVICE_SCAN_MS = 3_000

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
  private scan: ReturnType<typeof setInterval> | null = null
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
    this.stopDeviceScan()
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
    // Зеркало НЕ переключаем на каждом открытии — только после неудачи.
    //
    // Раньше счётчик рос при каждом open(), и стоило соединению несколько раз
    // упасть (а из-за CSP оно падало постоянно), как клиент навсегда уезжал
    // с cub.rip на соседнее зеркало. Устройства при этом не встречаются:
    // телевизор сидит на cub.rip, а мы спрашиваем совсем другой сервер, и
    // список законно приходит пустым.
    const host = SOCKET_MIRRORS[this.mirror % SOCKET_MIRRORS.length]
    const url = `wss://${host}:${SOCKET_PORT}`

    this.handlers.onState?.('connecting')
    try {
      this.ws = new WebSocket(url)
    } catch {
      this.scheduleReconnect()
      return
    }

    this.ws.addEventListener('open', () => {
      // Зеркало сработало — возвращаемся к первому, чтобы следующая же
      // случайная потеря связи не увела нас с cub.rip навсегда.
      this.mirror = 0
      this.handlers.onState?.('open')
      this.startPing()

      // Первым делом — check_token, как делает Лампа: у неё это подписано
      // на событие открытия сокета (`Socket.listener.follow('open',
      // checkAccountValidity)`, внутри `permit.token && send('check_token')`).
      // Похоже, именно им сервер признаёт соединение своим и заводит его
      // в группу аккаунта; без него список устройств приходил пустым.
      this.send('check_token', {})

      this.requestDevices()

      // И повторяем запрос, как Лампа: у неё в окне трансляции стоит
      // setInterval на 3 секунды. Устройство могло ещё не подключиться
      // к моменту нашего первого запроса.
      this.startDeviceScan()
    })

    this.ws.addEventListener('message', (ev) => this.onMessage(ev))

    this.ws.addEventListener('close', () => {
      this.stopPing()
      this.stopDeviceScan()
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
      // Сервер шлёт список широко, поэтому отсеиваем себя и служебную запись
      // «CUB» — ровно как это делает Лампа в окне трансляции.
      const list = (Array.isArray(result.data) ? (result.data as CubDevice[]) : [])
        .filter((d) => d && d.name !== 'CUB' && d.device_id !== this.uid)
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

  /**
   * Периодический опрос устройств — ровно как у Лампы в окне трансляции
   * (setInterval на 3 секунды). Список составляет сервер из тех, кто сейчас
   * на связи, и телевизор мог подключиться позже нашего первого запроса.
   */
  private startDeviceScan(): void {
    this.stopDeviceScan()
    this.scan = setInterval(() => {
      if (this.ws?.readyState === WebSocket.OPEN) this.requestDevices()
    }, DEVICE_SCAN_MS)
  }

  private stopDeviceScan(): void {
    if (this.scan) { clearInterval(this.scan); this.scan = null }
  }

  private scheduleReconnect(): void {
    this.handlers.onState?.('closed')
    // Не сработало — вот теперь пробуем следующее зеркало, как перебор
    // soc_mirrors у Лампы.
    this.mirror++
    setTimeout(() => { if (!this.closedByUs) this.open() }, 2000)
  }
}
