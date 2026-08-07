import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { apiClient, ApiError } from '@/lib/api/client'
import {
  applyFilters,
  countActive,
  countAlive,
  countVerified,
  countFacet,
  CLIENT_FILTER_LIMIT,
  EMPTY_CLIENT_FILTERS,
  type ClientFilters,
  type FacetKey,
} from '@/lib/filters'
import { pushRecentSearch, getRecentSearches } from '@/lib/recent-searches'
import { getItem, setItem, StorageKeys } from '@/lib/storage'
import {
  normalizeSortParam,
  sortItems,
  SORT_API_MAP,
  type SortValue,
  type TorrentItem,
} from '@/lib/torrents'

/**
 * Состояние поиска. Живёт на уровне модуля, а не компонента, потому что
 * оболочки подменяются на лету: повернули планшет — раскладка сменилась,
 * а выдача должна остаться на месте и не запрашиваться заново.
 *
 * Главное отличие от прежнего устройства: запрос уходит на сервер БЕЗ
 * фильтров, ровно один на поисковую фразу. Отбор и счётчики считаются здесь
 * же по полученному. Так счётчики вообще становятся возможны (по
 * отфильтрованному ответу их не посчитать) и заодно исчезают лишние запросы:
 * замер на живых данных — поиск с тремя фильтрами делал четыре запроса
 * и тянул около мегабайта вместо одного на 262 КБ.
 */
const SEARCH_TIMEOUT_MS = 20_000

const query = ref('')
const activeQuery = ref('')
const sort = ref<SortValue>('sid')
const filters = ref<ClientFilters>({ ...EMPTY_CLIENT_FILTERS })
const recent = ref<string[]>([])
const booted = ref(false)

function errorText(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 429) return 'Слишком много запросов подряд. Подождите минуту.'
    if (err.status === 401 || err.status === 403) return 'Доступ к поиску закрыт.'
    return `Сервер ответил ошибкой ${err.status}.`
  }
  if (err instanceof Error && err.name === 'AbortError') {
    return 'Поиск занял слишком долго и был прерван.'
  }
  if (err instanceof Error && err.message === 'Failed to fetch') {
    return 'Нет связи с сервером.'
  }
  return 'Поиск не удался.'
}

export function useSearch() {
  const route = useRoute()
  const router = useRouter()

  const torrents = useQuery({
    queryKey: computed(() => ['torrents', activeQuery.value] as const),
    enabled: computed(() => activeQuery.value.length > 0),
    queryFn: async ({ signal, queryKey }): Promise<TorrentItem[]> => {
      const search = queryKey[1]
      if (!search) return []
      const items = await apiClient.getTorrents(
        { search, sort: SORT_API_MAP[sort.value] },
        { timeoutMs: SEARCH_TIMEOUT_MS, signal },
      )
      return Array.isArray(items) ? (items as TorrentItem[]) : []
    },
  })

  const allItems = computed<TorrentItem[]>(() => torrents.data.value ?? [])
  const items = computed(() => sortItems(applyFilters(allItems.value, filters.value), sort.value))

  const facets = {
    quality: computed(() => countFacet(allItems.value, filters.value, 'quality')),
    tracker: computed(() => countFacet(allItems.value, filters.value, 'tracker')),
    year: computed(() => countFacet(allItems.value, filters.value, 'year')),
    voice: computed(() => countFacet(allItems.value, filters.value, 'voice')),
    type: computed(() => countFacet(allItems.value, filters.value, 'type')),
    size: computed(() => countFacet(allItems.value, filters.value, 'size')),
    season: computed(() => countFacet(allItems.value, filters.value, 'season')),
  }

  function syncUrl() {
    const q: Record<string, string> = {}
    if (activeQuery.value) q.s = activeQuery.value
    if (sort.value !== 'sid') q.sort = sort.value

    const f = filters.value
    for (const key of ['quality', 'tracker', 'year', 'voice', 'type', 'size', 'season'] as const) {
      if (f[key].length) q[key] = f[key].join(',')
    }
    if (f.hdr) q.hdr = '1'
    if (f.aliveOnly) q.alive = '1'
    if (f.refine.trim()) q.refine = f.refine.trim()
    if (f.exclude.trim()) q.exclude = f.exclude.trim()

    void router.replace({ path: route.path, query: q })
  }

  function readUrl() {
    const q = route.query
    const str = (v: unknown) => (typeof v === 'string' ? v : '')
    const list = (v: unknown) => str(v).split(',').filter(Boolean)

    filters.value = {
      quality: list(q.quality),
      tracker: list(q.tracker),
      year: list(q.year),
      voice: list(q.voice),
      type: list(q.type),
      size: list(q.size),
      season: list(q.season),
      hdr: str(q.hdr) === '1',
      aliveOnly: str(q.alive) === '1',
      refine: str(q.refine),
      exclude: str(q.exclude),
    }

    const s = normalizeSortParam(str(q.sort)) || normalizeSortParam(getItem(StorageKeys.sort))
    if (s) sort.value = s

    // Приём «поделиться»: система присылает текст под разными именами.
    const shared = [q.s, q.text, q.title].map(str).find((v) => v.trim().length > 0)
    if (shared) {
      query.value = shared.trim()
      activeQuery.value = shared.trim()
    }
  }

  /** Вызывается оболочкой один раз: разбирает адрес и при необходимости ищет. */
  function boot() {
    if (booted.value) return
    booted.value = true
    recent.value = getRecentSearches()
    readUrl()
  }

  function search(text?: string) {
    const q = (text ?? query.value).trim()
    if (!q) return
    query.value = q
    activeQuery.value = q
    setItem(StorageKeys.search, q)
    recent.value = pushRecentSearch(q)
    syncUrl()
  }

  function setSort(value: SortValue) {
    sort.value = value
    setItem(StorageKeys.sort, value)
    syncUrl()
  }

  /** Переключает значение внутри группы: выбрано — снять, нет — добавить. */
  function toggle(key: FacetKey, value: string) {
    const current = filters.value[key]
    const next = current.includes(value)
      ? current.filter((v) => v !== value)
      : [...current, value]
    filters.value = { ...filters.value, [key]: next }
    syncUrl()
  }

  function setFlag(key: 'hdr' | 'aliveOnly', value: boolean) {
    filters.value = { ...filters.value, [key]: value }
    syncUrl()
  }

  function setText(key: 'refine' | 'exclude', value: string) {
    filters.value = { ...filters.value, [key]: value }
    syncUrl()
  }

  function reset() {
    filters.value = { ...EMPTY_CLIENT_FILTERS }
    syncUrl()
  }

  function clear() {
    query.value = ''
    activeQuery.value = ''
    filters.value = { ...EMPTY_CLIENT_FILTERS }
    void router.replace({ path: '/' })
  }

  return {
    query,
    activeQuery,
    sort,
    filters,
    recent,
    facets,
    allItems,
    items,

    total: computed(() => allItems.value.length),
    shown: computed(() => items.value.length),
    alive: computed(() => countAlive(allItems.value)),
    verified: computed(() => countVerified(allItems.value)),
    activeCount: computed(() => countActive(filters.value)),

    isLoading: computed(() => torrents.isLoading.value),
    isFetching: computed(() => torrents.isFetching.value),
    error: computed(() => (torrents.isError.value ? errorText(torrents.error.value) : '')),

    /**
     * Выдача, на которой отбор в браузере перестаёт быть бесплатным.
     * Замер: широкий запрос отдаёт 1597 раздач и 2 МБ. Числа тут честнее
     * молчания — человек хотя бы поймёт, почему подтормаживает.
     */
    isHuge: computed(() => allItems.value.length > CLIENT_FILTER_LIMIT),

    boot,
    search,
    setSort,
    toggle,
    setFlag,
    setText,
    reset,
    clear,
  }
}
