/**
 * Что мы помним между заходами.
 *
 * Имена ключей унаследованы от прежнего интерфейса намеренно: у людей уже
 * лежат в браузере адрес TorrServer и ключ API, и менять имена значило бы
 * заставить их вводить всё заново.
 *
 * Из прежнего набора выброшены ключи, которым нечего хранить: режим
 * «стекло/плоско», язык интерфейса, вкладки настроек и отметка об уборке
 * старого service worker — всех этих возможностей больше нет. Тема переехала
 * в useTheme под собственным ключом.
 */
export const StorageKeys = {
  apiKey: 'api_key',
  devKey: 'dev_key',
  torrServerUrl: 'jacredTorServerUrl',
  torrServerLogin: 'jacredTorServerLogin',
  torrServerPassword: 'jacredTorServerPassword',
  search: 'search',
  sort: 'sort',
  exact: 'exact',
  recentSearches: 'jacredRecentSearches',
  /** Свёрнута ли панель фильтров на большом экране. */
  panelCollapsed: 'jbPanelCollapsed',
} as const

export type StorageKey = (typeof StorageKeys)[keyof typeof StorageKeys]

export function getItem(key: StorageKey): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    // Приватный режим или заблокированное хранилище: не помним, но и не падаем.
    return null
  }
}

export function setItem(key: StorageKey, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    /* см. выше */
  }
}

export function removeItem(key: StorageKey): void {
  try {
    localStorage.removeItem(key)
  } catch {
    /* см. выше */
  }
}

/**
 * Ключ API. На jac.black поиск открыт всем и ключ не нужен — проверено,
 * запрос без ключа отвечает 200. Но сборка может стоять и с закрытым
 * поиском, поэтому заголовок отправляем, если ключ задан.
 */
export function getApiKey(): string {
  return getItem(StorageKeys.apiKey) ?? ''
}

export function getDevKey(): string {
  return getItem(StorageKeys.devKey) ?? ''
}
