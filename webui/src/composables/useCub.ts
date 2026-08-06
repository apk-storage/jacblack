import { ref, shallowRef } from 'vue'
import { CubSocket, type CubDevice, type CubSocketState } from '@/lib/cub/socket'
import { buildLaunchEval, type LampaLaunch } from '@/lib/cub/eval-payload'
import { loadAccount, saveAccount, clearAccount, loginWithCode, type CubAccount } from '@/lib/cub/auth'

/**
 * Высокоуровневый доступ к функции «В Лампе».
 *
 * Сшивает три части: вход в cub.rip (account), сокет-пульт (список устройств) и
 * запуск раздачи через terminal_eval. UI (кнопка) работает только с этим
 * composable, деталей протокола не знает.
 *
 * Один экземпляр на приложение — состояние (аккаунт, устройства, сокет) общее.
 */

const account = shallowRef<CubAccount | null>(loadAccount())
const devices = ref<CubDevice[]>([])
const socketState = ref<CubSocketState>('idle')
let socket: CubSocket | null = null

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
  }

  /** Поднять сокет, если есть аккаунт. Идемпотентно. */
  function connect(): void {
    if (!account.value || socket) return
    socket = new CubSocket(account.value, {
      onState: (s) => { socketState.value = s },
      onDevices: (list) => { devices.value = list },
    })
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
   * Запустить раздачу на устройстве. Требует, чтобы на ТВ был задан код
   * терминала и тот же код введён здесь (`setTerminalCode`).
   *
   * Порядок как у Лампы: сначала активируем терминал присланным кодом, следом
   * шлём eval с открытием карточки и добавлением magnet.
   */
  function launch(device: CubDevice, release: LampaLaunch): void {
    if (!socket || !socket.connected) throw new Error('Нет связи с CUB — войдите в аккаунт')
    if (!terminalCode.value) throw new Error('Не задан код терминала (его нужно включить в Лампе на ТВ)')

    const js = buildLaunchEval(release)
    socket.terminalActivate(terminalCode.value)
    // Небольшая пауза, чтобы устройство успело активировать терминал до eval.
    setTimeout(() => {
      try { socket?.terminalEval(terminalCode.value, js) } catch { /* переподключение поднимет заново */ }
    }, 300)
  }

  // Автоподключение при наличии сохранённого аккаунта.
  if (account.value && !socket) connect()

  return {
    account,
    devices,
    socketState,
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
