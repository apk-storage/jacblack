/**
 * Контроллер сокета CUB — «пульт» Лампы для функции «В Лампе».
 *
 * Протокол восстановлен реверсом app.min.js Лампы (2026-08-06, сверено
 * повторно 2026-08-20). Полный разбор — в
 * personal-hub/lampac/JACBLACK-V-LAMPE.md. Кратко:
 *
 * - Лампа держит WebSocket к `wss://<зеркало>:8443`. Устройства одного аккаунта
 *   CUB видят команды друг друга — сервер роутит по полю `account`.
 * - Отправка: JSON с полями device_id/name/method/version/account/premium/terminal.
 *   Пинг — строкой `'ping'`, ответ — `'pong'`.
 * - Приём: JSON, ветвление по `result.method`. Нам приходят `devices` (список
 *   устройств аккаунта), `terminal_result` (ответ на eval) и `logoff` (сервер
 *   не признал аккаунт).
 *
 * Запуск раздачи идёт через `terminal_eval`: на устройство шлётся JS, который
 * открывает карточку (imdb→tmdb) и добавляет magnet в его TorrServer. Требует,
 * чтобы на устройстве был задан код терминала (`terminal_access`).
 *
 * ── Почему мы держим ВСЕ зеркала сразу ────────────────────────────────────
 *
 * Замер 20.08.2026: `cub.red`, `cub.best`, `kurwa-bober.ninja` и `nackhui.com`
 * отдают ОДИН И ТОТ ЖЕ список устройств — это один кластер. А `cub.black` живёт
 * на своём адресе (78.24.93.19, без Cloudflare, свой nginx и свой сертификат) и
 * список у него ПУСТОЙ, то есть база устройств у него отдельная.
 *
 * Лампа на телевизоре перебирает свои зеркала (`['cub.best','cub.black',
 * 'kurwa-bober.ninja','nackhui.com']`) и может сесть на любое, в том числе на
 * изолированный `cub.black`. Клиент, подключённый к одному зеркалу, такой
 * телевизор не увидит НИКОГДА — и это неотличимо от «телевизор не вошёл в
 * аккаунт», потому что список законно приходит пустым.
 *
 * Поэтому здесь не перебор, а пул: соединение с каждым зеркалом одновременно,
 * устройства объединяются по uid, команды уходят во все открытые сокеты.
 * Стоимость — несколько лишних соединений; выигрыш — функция перестаёт зависеть
 * от того, куда попал телевизор.
 */

/**
 * Зеркала сокета CUB. Только СВОИ: через сокет уходит объект аккаунта, поэтому
 * чужие домены сюда не добавлять — `cub.tv`, например, отвечает 200, но он не
 * наш.
 *
 * `kurwa-bober.ninja`, `nackhui.com` и `cubnotrip.top` — наши, но ТОЛЬКО как
 * сокет: личного кабинета на них нет, в список входа (CubController) они не
 * идут. `cub.rip` умер к 20.08.2026 и из перебора убран — он больше не
 * резолвится вовсе. `cubnotrip.top` соединение отвергает, оставлен на случай
 * возвращения.
 */
const SOCKET_MIRRORS = [
  'cub.red',
  'cub.best',
  'cub.black',
  'kurwa-bober.ninja',
  'nackhui.com',
  'cubnotrip.top',
]
const SOCKET_PORT = 8443

/**
 * Пинг. У Лампы `timeping = 5000`; берём столько же — сервер закрывать
 * соединение за молчание не замечен, но держаться ближе к оригиналу дешевле,
 * чем выяснять это на живых людях.
 */
const PING_INTERVAL_MS = 5_000

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
   * CUB отверг аккаунт. У Лампы этот метод приводит к `Account.logoff()` —
   * то есть к выходу из учётки; значит сервер считает её недействительной и
   * устройств не пришлёт никогда. Без обработки это выглядит как «связь есть,
   * а устройств нет», и человек идёт чинить телевизор вместо повторного входа.
   */
  onLogoff?: (data: unknown) => void
  /**
   * Состояние каждого зеркала — для видимых техданных. «Соединение не
   * открылось» без имён зеркал не отличает мёртвое зеркало от сервера,
   * который нас выгнал.
   */
  onMirrors?: (report: string) => void
  /**
   * device_id устройства, ОТКЛИКНУВШЕГОСЯ на наш код терминала.
   *
   * Единственный надёжный признак «моего» устройства. Сервер CUB отдаёт список
   * ВСЕХ подключённых (138 штук в замере 20.08) без пометки владельца — по нему
   * свой телевизор от чужого не отличить. А `terminal_result` присылает только
   * устройство, у которого код совпал, и в ответе стоит его device_id.
   */
  onOwnDevice?: (deviceId: string) => void
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

/** Одно соединение с одним зеркалом. */
type Link = {
  host: string
  ws: WebSocket | null
  state: 'нет связи' | 'подключаюсь' | 'на связи' | string
  devices: CubDevice[]
  ping: ReturnType<typeof setInterval> | null
  scan: ReturnType<typeof setInterval> | null
  retry: ReturnType<typeof setTimeout> | null
  /** Сколько раз уже пробивали кодом на этом соединении (ограничено сверху). */
  probes: number
}

/**
 * Сколько раз повторять пробу кода терминала, пока свой ТВ не опознан.
 * Каждые DEVICE_SCAN_MS (3 с), то есть ~10 попыток ≈ 30 секунд — этого хватает,
 * чтобы ТВ успел подключиться. Дальше НЕ долбим: список устройств всё равно
 * приходит периодически, а лишние сообщения на сокет CUB (который и так под
 * нагрузкой) ни к чему. Смена кода счётчик сбрасывает.
 */
const MAX_PROBE_CYCLES = 10

export class CubSocket {
  private links: Link[] = []
  private closedByUs = false
  /**
   * Код терминала. Лампа кладёт его в КАЖДОЕ сообщение
   * (`data.terminal = Storage.get('terminal_access','')`), а мы слали пустую
   * строку — то есть представлялись сервером как устройство без терминала.
   */
  private terminal = ''
  /** Опознали ли уже хоть одно своё устройство — чтобы не пробить кодом вечно. */
  private ownIdentified = false
  private readonly account: CubAccount
  private readonly handlers: Handlers
  private readonly uid = selfDeviceId()

  constructor(account: CubAccount, handlers: Handlers = {}) {
    this.account = account
    this.handlers = handlers
  }

  /** Задать код терминала — он уходит в каждом сообщении, как у Лампы. */
  setTerminal(code: string): void {
    const novyi = String(code || '')
    if (novyi !== this.terminal) {
      this.ownIdentified = false          // сменили код — пробуем заново
      for (const l of this.links) l.probes = 0
    }
    this.terminal = novyi
  }

  get connected(): boolean {
    return this.links.some((l) => l.ws?.readyState === WebSocket.OPEN)
  }

  connect(): void {
    this.closedByUs = false
    if (this.links.length === 0) {
      this.links = SOCKET_MIRRORS.map((host) => ({
        host, ws: null, state: 'нет связи', devices: [], ping: null, scan: null, retry: null, probes: 0,
      }))
    }
    this.handlers.onState?.('connecting')
    for (const link of this.links) this.open(link)
  }

  close(): void {
    this.closedByUs = true
    for (const link of this.links) {
      this.stopTimers(link)
      try { link.ws?.close() } catch { /* ignore */ }
      link.ws = null
      link.state = 'нет связи'
      link.devices = []
    }
    this.handlers.onState?.('closed')
  }

  /** Запросить список устройств. Лампа шлёт это раз в 3 с при выборе. */
  requestDevices(): void {
    this.broadcast('devices', {})
  }

  /**
   * Активировать терминал присланным кодом. Должен совпасть с тем, что задан на
   * ТВ (Storage 'terminal_access'). Без успешной активации eval не выполнится.
   *
   * Уходит во ВСЕ зеркала: на каком из них сидит нужный телевизор — заранее
   * неизвестно, а лишняя команда для чужого зеркала безвредна (там её просто
   * некому исполнить).
   */
  terminalActivate(code: string): void {
    this.broadcast('terminal_activate', { code })
  }

  /**
   * Выполнить JS на устройстве. code — тот же терминал-код; eval — тело.
   * Устройство сверяет code и делает `eval(eval)`, ответ придёт в
   * terminal_result.
   */
  terminalEval(code: string, js: string): void {
    this.broadcast('terminal_eval', { code, eval: js })
  }

  // ── внутреннее ──────────────────────────────────────────────────────────

  private open(link: Link): void {
    this.stopTimers(link)
    link.state = 'подключаюсь'
    this.reportMirrors()

    try {
      link.ws = new WebSocket(`wss://${link.host}:${SOCKET_PORT}`)
    } catch {
      link.state = 'браузер отказал'
      this.reportMirrors()
      this.scheduleReconnect(link)
      return
    }

    link.ws.addEventListener('open', () => {
      link.state = 'на связи'
      this.handlers.onState?.('open')
      this.reportMirrors()

      link.ping = setInterval(() => {
        if (link.ws?.readyState === WebSocket.OPEN) {
          try { link.ws.send('ping') } catch { /* ignore */ }
        }
      }, PING_INTERVAL_MS)

      // check_token первым — так делает Лампа: у неё это подписано на событие
      // открытия сокета. Похоже, им сервер признаёт соединение своим и заводит
      // его в группу аккаунта; без него список устройств приходил пустым.
      this.sendTo(link, 'check_token', {})
      this.sendTo(link, 'devices', {})

      // Проба «кто мой» — сразу при открытии, если код терминала уже задан.
      // Раньше она уходила только при РУЧНОМ вводе кода (setTerminal), поэтому с
      // уже сохранённым кодом свой ТВ не опознавался и список не схлопывался.
      link.probes = 0
      if (this.terminal) { this.sendTo(link, 'terminal_activate', { code: this.terminal }); link.probes = 1 }

      // Повторяем запрос, как Лампа: у неё в окне трансляции setInterval на 3 с.
      // Устройство могло ещё не подключиться к моменту первого запроса; заодно,
      // пока свой ТВ не опознан, повторяем и пробу кода — вдруг он подключился
      // позже нас.
      link.scan = setInterval(() => {
        if (link.ws?.readyState !== WebSocket.OPEN) return
        this.sendTo(link, 'devices', {})
        if (this.terminal && !this.ownIdentified && link.probes < MAX_PROBE_CYCLES) {
          this.sendTo(link, 'terminal_activate', { code: this.terminal })
          link.probes += 1
        }
      }, DEVICE_SCAN_MS)
    })

    link.ws.addEventListener('message', (ev) => this.onMessage(link, ev))

    link.ws.addEventListener('close', (ev) => {
      this.stopTimers(link)
      link.state = `закрыто (${ev.code})`
      link.devices = []
      this.publishDevices()
      this.reportMirrors()
      if (!this.closedByUs) {
        if (!this.connected) this.handlers.onState?.('closed')
        this.scheduleReconnect(link)
      }
    })

    link.ws.addEventListener('error', () => {
      link.state = 'ошибка связи'
      this.reportMirrors()
      try { link.ws?.close() } catch { /* ignore */ }
    })
  }

  private onMessage(link: Link, ev: MessageEvent): void {
    // Пинг-ответ — не JSON.
    if (ev.data === 'pong') return

    let result: { method?: string; data?: unknown; device_id?: string; uid?: string }
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
      link.devices = (Array.isArray(result.data) ? (result.data as CubDevice[]) : [])
        .filter((d) => d && d.name !== 'CUB' && d.device_id !== this.uid)
      this.publishDevices()
    } else if (result.method === 'terminal_result') {
      // Ответ пришёл от устройства, у которого КОД СОВПАЛ, — а отвечает оно
      // через ту же функцию `send`, что добавляет собственный device_id.
      // Это единственный надёжный признак «моего» устройства: сервер отдаёт
      // список всех подключённых (138 штук в замере 20.08) без всякой пометки
      // владельца, и отличить свой телевизор от чужого больше нечем.
      const otvetil = String(result.device_id || result.uid || '')
      if (otvetil) { this.ownIdentified = true; this.handlers.onOwnDevice?.(otvetil) }
      this.handlers.onTerminalResult?.(result.data)
    } else if (result.method === 'logoff') {
      // Сервер не признал аккаунт. У Лампы это `Account.logoff()` — выход из
      // учётки. Пока мы это молчали, картина выглядела как «связь есть, но
      // устройств нет», и человек шёл настраивать телевизор впустую.
      this.handlers.onLogoff?.(result.data)
    }
  }

  /**
   * Свести устройства со всех зеркал в один список.
   *
   * Кластерные зеркала отдают один и тот же набор, поэтому дедуплицируем по
   * uid — иначе один телевизор показался бы человеку четыре раза.
   */
  private publishDevices(): void {
    const seen = new Map<string, CubDevice>()
    for (const link of this.links) {
      for (const d of link.devices) {
        const key = String(d.uid ?? d.device_id ?? '')
        if (key && !seen.has(key)) seen.set(key, d)
      }
    }
    this.handlers.onDevices?.([...seen.values()])
  }

  private reportMirrors(): void {
    this.handlers.onMirrors?.(
      this.links.map((l) => `${l.host}: ${l.state}`).join(' · '),
    )
  }

  /**
   * Сборка и отправка сообщения ровно по формату Лампы (функция send в
   * app.min.js): к данным добавляются обязательные метаполя. account — то, чем
   * сервер роутит команду к устройствам того же пользователя.
   */
  private sendTo(link: Link, method: string, data: Record<string, unknown>): void {
    if (link.ws?.readyState !== WebSocket.OPEN) return
    const payload = {
      ...data,
      device_id: this.uid,
      name: 'JacBlack - web',
      method,
      version: 1,
      account: this.account,
      premium: false,
      // Лампа кладёт сюда `Storage.get('terminal_access','')` — код терминала
      // того устройства, что отправляет сообщение. Мы слали пустую строку.
      terminal: this.terminal,
    }
    try {
      link.ws.send(JSON.stringify(payload))
    } catch {
      /* соединение оборвалось — переподключение поднимет заново */
    }
  }

  private broadcast(method: string, data: Record<string, unknown>): void {
    for (const link of this.links) this.sendTo(link, method, data)
  }

  private stopTimers(link: Link): void {
    if (link.ping) { clearInterval(link.ping); link.ping = null }
    if (link.scan) { clearInterval(link.scan); link.scan = null }
    if (link.retry) { clearTimeout(link.retry); link.retry = null }
  }

  private scheduleReconnect(link: Link): void {
    if (link.retry) clearTimeout(link.retry)
    link.retry = setTimeout(() => {
      if (!this.closedByUs) this.open(link)
    }, 5000)
  }
}
