import { computed, ref, shallowRef } from 'vue'
import { CubSocket, type CubDevice, type CubSocketState } from '@/lib/cub/socket'
import { buildLaunchEval, type LampaLaunch } from '@/lib/cub/eval-payload'
import { loadAccount, saveAccount, clearAccount, loginWithCode, type CubAccount } from '@/lib/cub/auth'

/**
 * Высокоуровневый доступ к функции «В Лампе».
 *
 * Сшивает три части: вход в аккаунт Лампы (account), сокет-пульт (список устройств) и
 * запуск раздачи через terminal_eval. UI (кнопка) работает только с этим
 * composable, деталей протокола не знает.
 *
 * Один экземпляр на приложение — состояние (аккаунт, устройства, сокет) общее.
 */

const account = shallowRef<CubAccount | null>(loadAccount())
const devices = ref<CubDevice[]>([])
const socketState = ref<CubSocketState>('idle')
let socket: CubSocket | null = null

/**
 * device_id устройств, откликнувшихся на наш код терминала, — то есть СВОИХ.
 *
 * CUB отдаёт список всех подключённых без пометки владельца (138 штук в замере
 * 20.08), поэтому свой телевизор виден вперемешку с чужими. Как только человек
 * задал код и хоть одно устройство отозвалось — показываем только их.
 */
const ownDeviceIds = ref<Set<string>>(new Set())

/**
 * Техданные для диалога: сколько сообщений пришло от CUB и каким было
 * последнее. Пустой список устройств сам по себе ничего не объясняет —
 * молчит сервер или отвечает пустотой, — а лезть в консоль браузера
 * с телефона неудобно.
 */
const received = ref(0)
const lastMethod = ref('')

/**
 * Состояние каждого зеркала строкой.
 *
 * Без этого «соединение не открылось» — тупик: непонятно, к какому серверу
 * стучались и почему не вышло. Зеркала ведут себя по-разному, и разбирать это
 * по консоли браузера на телевизоре невозможно.
 */
const lastAttempt = ref('')

/** CUB отверг аккаунт — устройств не будет, надо входить заново. */
const rejected = ref(false)

/** Куда приходит ответ устройства на текущую команду. */
let ответ: ((d: unknown) => void) | null = null

/** Код терминала (`terminal_access`), заданный на ТВ. Хранится локально. */
const TERMINAL_KEY = 'jb_cub_terminal'
const terminalCode = ref<string>(readTerminal())

function readTerminal(): string {
  try { return localStorage.getItem(TERMINAL_KEY) || '' } catch { return '' }
}

export function useCub() {
  const authorized = () => account.value != null

  async function login(code: string): Promise<void> {
    const acc = await loginWithCode(code)
    account.value = acc
    saveAccount(acc)
    connect()
  }

  function logout(): void {
    account.value = null
    clearAccount()
    disconnect()
  }

  function setTerminalCode(code: string): void {
    terminalCode.value = code.trim()
    try { localStorage.setItem(TERMINAL_KEY, terminalCode.value) } catch { /* ignore */ }
    // Сокет мог быть поднят раньше, чем человек ввёл код: он уходит в каждом
    // сообщении, поэтому обновляем и в живом соединении, а не только при старте.
    socket?.setTerminal(terminalCode.value)
    // Разослать пробную активацию: устройство с этим кодом откликнется
    // `terminal_result` со своим device_id, и мы опознаем его как своё —
    // после этого список схлопнется до своих. Код сменился — начинаем заново.
    ownDeviceIds.value = new Set()
    if (terminalCode.value) socket?.terminalActivate(terminalCode.value)
  }

  /** Поднять сокет, если есть аккаунт. Идемпотентно. */
  function connect(): void {
    if (!account.value || socket) return
    rejected.value = false
    socket = new CubSocket(account.value, {
      onState: (s) => { socketState.value = s },
      onDevices: (list) => { devices.value = list },
      onTerminalResult: (d) => ответ?.(d),
      onLogoff: () => { rejected.value = true },
      onMirrors: (report) => { lastAttempt.value = report },
      onOwnDevice: (id) => {
        if (id && !ownDeviceIds.value.has(id)) {
          ownDeviceIds.value = new Set([...ownDeviceIds.value, id])
        }
      },
      onAny: (method, size) => {
        received.value += 1
        lastMethod.value = size ? `${method} (${size})` : method
      },
    })
    // Код терминала уходит в каждом сообщении — так делает Лампа.
    socket.setTerminal(terminalCode.value)
    socket.connect()
  }

  function disconnect(): void {
    socket?.close()
    socket = null
    devices.value = []
    socketState.value = 'idle'
  }

  function refreshDevices(): void {
    socket?.requestDevices()
  }

  /**
   * Отправка раздачи на устройство — с ожиданием отклика.
   *
   * Раньше команда уходила «в никуда»: activate, через 300 мс eval, и всё —
   * человек видел, что устройство в списке есть, а на телевизоре ничего не
   * происходило, и понять почему было нельзя. Между тем Лампа отвечает:
   * на успешную активацию шлёт `terminal_result` со словами «Terminal access
   * activated», а после eval возвращает его результат — включая текст
   * исключения, если код упал.
   *
   * Поэтому ждём отклик и возвращаем его наружу. Молчание тоже значимо: оно
   * означает, что устройство не приняло код терминала или в его сборке Лампы
   * выключена обработка команд сокета (`lampa_settings.socket_methods`) — без
   * неё terminal_eval игнорируется целиком.
   */
  async function launch(_device: CubDevice, release: LampaLaunch): Promise<string> {
    if (!socket || !socket.connected) throw new Error('Нет связи с CUB — войдите в аккаунт')
    if (!terminalCode.value)
      throw new Error('Не задан код терминала — включите Терминал в Лампе на устройстве')

    const дождаться = (сколько: number) =>
      new Promise<string | null>((готово) => {
        const таймер = setTimeout(() => { ответ = null; готово(null) }, сколько)
        ответ = (данные) => {
          clearTimeout(таймер)
          готово(typeof данные === 'string' ? данные : JSON.stringify(данные))
        }
      })

    socket.terminalActivate(terminalCode.value)
    const активация = await дождаться(6000)
    if (активация === null) {
      throw new Error(
        'Устройство не отозвалось на код терминала. Проверьте, что Лампа открыта, ' +
        'а код в её настройках совпадает с указанным здесь.',
      )
    }

    socket.terminalEval(terminalCode.value, buildLaunchEval(release))
    const итог = await дождаться(12000)
    if (итог === null) {
      throw new Error(
        'Терминал принял код, но команда осталась без ответа. Обычно так бывает, ' +
        'когда в сборке Лампы выключена обработка команд сокета.',
      )
    }
    return итог
  }

  /**
   * Что показать человеку. Пока код терминала не задан или на него никто не
   * откликнулся — весь список (без него отфильтровать своих нечем, а прятать
   * всё — значит скрыть и настоящий телевизор). Как только своё устройство
   * опознано — только свои: 138 чужих в списке никому не нужны.
   */
  const visibleDevices = computed<CubDevice[]>(() => {
    if (ownDeviceIds.value.size === 0) return devices.value
    return devices.value.filter(
      (d) => ownDeviceIds.value.has(String(d.device_id)) || ownDeviceIds.value.has(String(d.uid)),
    )
  })

  /** Опознаны ли свои устройства — диалог по этому меняет подпись списка. */
  const ownKnown = computed(() => ownDeviceIds.value.size > 0)

  // Автоподключение при наличии сохранённого аккаунта.
  if (account.value && !socket) connect()

  return {
    account,
    devices,
    visibleDevices,
    ownKnown,
    socketState,
    received,
    lastMethod,
    lastAttempt,
    rejected,
    terminalCode,
    authorized,
    login,
    logout,
    setTerminalCode,
    connect,
    disconnect,
    refreshDevices,
    launch,
  }
}
