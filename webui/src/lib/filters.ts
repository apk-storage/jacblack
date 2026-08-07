import type { TorrentItem } from '@/lib/torrents'

/**
 * Отбор и подсчёт — целиком в браузере.
 *
 * Почему не на сервере, как было раньше. Фильтры уходили в запрос, и ответ
 * уже не содержал отфильтрованного — значит счётчик у пункта «rutracker»
 * показывал бы ноль, стоило выбрать «rutor». Считать фасеты можно только
 * по полной выдаче.
 *
 * Побочная выгода замерена на живых данных: поиск с тремя фильтрами делал
 * четыре запроса и тянул около мегабайта; теперь это один запрос на 262 КБ,
 * а смена фильтра отвечает мгновенно.
 *
 * Ограничение тоже замерено: широкий запрос («сезон») отдаёт 1597 раздач и
 * 2 МБ. Выше порога отбор возвращается на сервер, а счётчики прячутся —
 * см. CLIENT_FILTER_LIMIT.
 */
export const CLIENT_FILTER_LIMIT = 1000

export type SizeBucket = {
  key: string
  label: string
  /** Границы в гигабайтах, верхняя не включается. */
  from: number
  to: number
}

/**
 * Ступени размера вместо ползунка. Замер по живой выдаче: от 0.03 ГБ до
 * 110.61 при медиане 8.74 — три порядка. На линейном ползунке девять
 * десятых хода пришлись бы на десятую часть раздач, а пальцем на телефоне
 * в такое не попасть.
 */
export const SIZE_BUCKETS: SizeBucket[] = [
  { key: 'xs', label: 'до 2 ГБ', from: 0, to: 2 },
  { key: 's', label: '2–5', from: 2, to: 5 },
  { key: 'm', label: '5–10', from: 5, to: 10 },
  { key: 'l', label: '10–20', from: 10, to: 20 },
  { key: 'xl', label: '20–50', from: 20, to: 50 },
  { key: 'xxl', label: 'больше 50', from: 50, to: Infinity },
]

const GB = 1024 ** 3

export function sizeBucketKey(bytes: number | null | undefined): string {
  const gb = (Number(bytes) || 0) / GB
  if (gb <= 0) return ''
  const found = SIZE_BUCKETS.find((b) => gb >= b.from && gb < b.to)
  return found?.key ?? ''
}

export type ClientFilters = {
  quality: string[]
  tracker: string[]
  year: string[]
  voice: string[]
  type: string[]
  size: string[]
  /**
   * Номера сезонов. У сериала это главный способ сузить выдачу: «Пацаны»
   * отдают под полтысячи раздач всех пяти сезонов разом, и без сезона
   * выбирать не из чего.
   */
  season: string[]
  hdr: boolean
  aliveOnly: boolean
  refine: string
  exclude: string
}

export const EMPTY_CLIENT_FILTERS: ClientFilters = {
  quality: [],
  tracker: [],
  year: [],
  voice: [],
  type: [],
  size: [],
  season: [],
  hdr: false,
  aliveOnly: false,
  refine: '',
  exclude: '',
}

/** Ключи, которые попадают в адрес страницы, чтобы выдачей можно было поделиться. */
export const FILTER_URL_KEYS = [
  'quality',
  'tracker',
  'year',
  'voice',
  'type',
  'size',
  'season',
  'hdr',
  'alive',
  'refine',
  'exclude',
] as const

export type FacetKey = 'quality' | 'tracker' | 'year' | 'voice' | 'type' | 'size' | 'season'

/** Пусто значит «не ограничиваем», а не «ничего не подходит». */
function passesList(values: string[], value: string): boolean {
  return values.length === 0 || values.includes(value)
}

function itemValues(item: TorrentItem, key: FacetKey): string[] {
  switch (key) {
    case 'quality':
      return item.quality == null || item.quality === '' ? [] : [String(item.quality)]
    case 'tracker':
      return item.tracker ? [item.tracker] : []
    case 'year':
      return item.relased == null || item.relased === '' ? [] : [String(item.relased)]
    case 'voice':
      return item.voices ?? []
    case 'type':
      return item.types ?? []
    case 'size': {
      const k = sizeBucketKey(item.size)
      return k ? [k] : []
    }
    case 'season':
      // Сборник несёт несколько сезонов сразу («1-3 сезоны»), и подходит под
      // выбор любого из них. Нулевой сезон — спецвыпуски, к выбору сезона он
      // отношения не имеет и в список не идёт.
      return (item.seasons ?? [])
        .map((s) => String(s))
        .filter((s) => s !== '' && s !== '0')
  }
}

/**
 * Подходит ли раздача под фильтры. `skip` исключает одну группу из проверки —
 * этим пользуется подсчёт фасетов.
 */
export function matches(
  item: TorrentItem,
  f: ClientFilters,
  skip?: FacetKey,
): boolean {
  const groups: FacetKey[] = ['quality', 'tracker', 'year', 'voice', 'type', 'size', 'season']

  for (const key of groups) {
    if (key === skip) continue
    const selected = f[key]
    if (selected.length === 0) continue

    const values = itemValues(item, key)
    // Раздача без значения в этой группе под явный выбор не подходит.
    if (values.length === 0) return false
    if (!values.some((v) => passesList(selected, v))) return false
  }

  if (f.hdr && String(item.videotype || '').toLowerCase() !== 'hdr') return false

  // Живой считаем всё, у чего сиды больше нуля, подтверждено оно или нет:
  // зелёная точка отмечает проверенные, дальше человек решает сам.
  if (f.aliveOnly && (Number(item.sid) || 0) <= 0) return false

  const title = (item.title || item.name || '').toLowerCase()
  const r = f.refine.trim().toLowerCase()
  const e = f.exclude.trim().toLowerCase()
  if (r && !title.includes(r)) return false
  if (e && title.includes(e)) return false

  return true
}

export function applyFilters(items: TorrentItem[], f: ClientFilters): TorrentItem[] {
  return items.filter((i) => matches(i, f))
}

export type FacetCount = { value: string; count: number }

/**
 * Счётчики по одной группе.
 *
 * Считаем по выдаче, отфильтрованной ВСЕМ, кроме этой же группы. Иначе
 * выбранное значение показывало бы своё число, а все соседние — нули, и
 * счётчики вместо помощи вводили бы в заблуждение.
 */
export function countFacet(
  items: TorrentItem[],
  f: ClientFilters,
  key: FacetKey,
): FacetCount[] {
  const counts = new Map<string, number>()

  for (const item of items) {
    if (!matches(item, f, key)) continue
    for (const value of itemValues(item, key)) {
      counts.set(value, (counts.get(value) ?? 0) + 1)
    }
  }

  const list = Array.from(counts, ([value, count]) => ({ value, count }))

  if (key === 'size') {
    const order = new Map(SIZE_BUCKETS.map((b, i) => [b.key, i]))
    return list.sort((a, b) => (order.get(a.value) ?? 0) - (order.get(b.value) ?? 0))
  }

  if (key === 'quality' || key === 'year') {
    return list.sort((a, b) => Number(b.value) - Number(a.value))
  }

  // Сезоны — по возрастанию, как их считает человек: первый, второй, третий.
  // По убыванию счётчика вышел бы бессмысленный порядок вроде «5, 1, 3».
  if (key === 'season') {
    return list.sort((a, b) => Number(a.value) - Number(b.value))
  }

  return list.sort((a, b) => b.count - a.count || a.value.localeCompare(b.value))
}

export function countActive(f: ClientFilters): number {
  let n = 0
  for (const key of ['quality', 'tracker', 'year', 'voice', 'type', 'size', 'season'] as const) {
    if (f[key].length) n += 1
  }
  if (f.hdr) n += 1
  if (f.aliveOnly) n += 1
  if (f.refine.trim()) n += 1
  if (f.exclude.trim()) n += 1
  return n
}

export function countAlive(items: TorrentItem[]): number {
  let n = 0
  for (const item of items) if ((Number(item.sid) || 0) > 0) n += 1
  return n
}

/**
 * Сколько раздач с проверенным числом раздающих.
 *
 * Человеку это важнее, чем кажется: непроверенное число — снимок из базы,
 * который бывает годовалым. Видя «проверено 42 из 174», он понимает, почему
 * у части строк полый контур, и не считает все числа одинаково надёжными.
 */
export function countVerified(items: TorrentItem[]): number {
  let n = 0
  for (const item of items) if (item.seedersLive) n += 1
  return n
}
