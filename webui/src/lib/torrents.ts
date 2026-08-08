/** Torrent item as returned by GET /api/v1.0/torrents (runtime projection). */
export type TorrentItem = {
  tracker?: string | null
  url?: string | null
  title?: string | null
  size?: number | null
  sizeName?: string | null
  createTime?: number | string | null
  updateTime?: number | string | null
  sid?: number | null
  pir?: number | null
  magnet?: string | null
  name?: string | null
  originalname?: string | null
  relased?: number | string | null
  videotype?: string | null
  /** Dolby Vision: «dv», «dvtv» или пусто. Отдельно от videotype — тот знает только sdr/hdr. */
  dv?: string | null
  quality?: number | string | null
  voices?: string[] | null
  seasons?: Array<number | string> | null
  types?: string[] | null
  media?: MediaSummary | null
  /** Проверено ли число раздающих сейчас — живым опросом либо свежим обходом. */
  seedersLive?: boolean | null
  /** Трекер не сообщает числа вовсе: у lostfilm счётчиков нет, единица в записи выдумана разбором. */
  seedersUnknown?: boolean | null
  /** Код IMDB, если он известен. */
  imdb?: string | null
}

/**
 * Сводка дорожек приходит с сервера уже разобранной.
 *
 * Считать её здесь было бы соблазнительно — заголовок под рукой, — но тогда
 * разбор кодека жил бы в двух местах сразу, на C# и на TypeScript, и они
 * разошлись бы. Поэтому фронт только показывает.
 *
 * `tracks` появляются лишь у раздач с разбором ffprobe: у остальных известен
 * только набор кодеков из заголовка, без привязки к дорожкам.
 */
export type MediaSummary = {
  video?: string | null
  audio?: string[] | null
  tracks?: Array<{
    codec?: string | null
    language?: string | null
    channels?: number | null
    title?: string | null
  }> | null
  subtitles?: string[] | null
}

export type SortValue = 'sid' | 'size' | 'date' | 'update'

export const SORT_API_MAP: Record<SortValue, string> = {
  sid: 'sid',
  size: 'size',
  date: 'create',
  update: 'update',
}

/** Подписи прямо здесь: интерфейс одноязычный, слоя переводов больше нет. */
export const SORT_OPTIONS: { value: SortValue; label: string }[] = [
  { value: 'sid', label: 'По сидам' },
  { value: 'size', label: 'По размеру' },
  { value: 'date', label: 'По дате' },
  { value: 'update', label: 'По обновлению' },
]

export type SearchFilters = {
  type: string
  tracker: string
  voice: string
  videotype: string
  year: string
  quality: string
  season: string
  refine: string
  exclude: string
}

export const EMPTY_FILTERS: SearchFilters = {
  type: '',
  tracker: '',
  voice: '',
  videotype: '',
  year: '',
  quality: '',
  season: '',
  refine: '',
  exclude: '',
}

export const URL_FILTER_KEYS = [
  'type',
  'tracker',
  'voice',
  'videotype',
  'year',
  'quality',
  'season',
  'refine',
  'exclude',
] as const

export function normalizeSortParam(val: string | null | undefined): SortValue | '' {
  const v = String(val || '').toLowerCase()
  if (v === 'create' || v === 'added') return 'date'
  if (v === 'pir') return 'sid'
  if (v === 'sid' || v === 'size' || v === 'date' || v === 'update') return v
  return ''
}

function toTimestamp(ts: number | string | null | undefined): number {
  if (ts == null || ts === '') return 0
  if (typeof ts === 'number') {
    const d = new Date(ts < 1e12 ? ts * 1000 : ts)
    return Number.isNaN(d.getTime()) ? 0 : d.getTime()
  }
  const d = new Date(ts)
  return Number.isNaN(d.getTime()) ? 0 : d.getTime()
}

export function formatDate(ts: number | string | null | undefined): string {
  if (ts == null || ts === '') return '—'
  let d: Date
  if (typeof ts === 'number') {
    d = new Date(ts < 1e12 ? ts * 1000 : ts)
  } else {
    d = new Date(ts)
  }
  if (Number.isNaN(d.getTime())) return '—'
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

export function formatQualityLabel(q: number | string | null | undefined): string {
  const n = Number(q)
  if (!n || !Number.isFinite(n) || n < 1) return ''
  if (n === 4320) return '8K'
  if (n === 2160) return '4K'
  if (n === 1440) return '1440p'
  return `${n}p`
}

/**
 * Подпись Dolby Vision. Сервер присылает «dv» или «dvtv» — ровно два значения,
 * столько же различает Лампа.
 *
 * Отдельно от качества намеренно: `videotype` знает только «sdr» и «hdr», и
 * DV-раздача в нём неотличима от обычной, хотя разница решает, покажет ли
 * устройство верные цвета.
 */
export function formatDvLabel(dv: string | null | undefined): string {
  const v = String(dv || '').toLowerCase()
  if (v === 'dvtv') return 'DV TV'
  return v === 'dv' ? 'DV' : ''
}

/**
 * Плашки дорожек одной строкой — для узкой вёрстки телефона.
 *
 * Большой экран рисует дорожки подробнее, по одной; здесь нужен короткий
 * список, который поместится в строку выдачи. Логика общая и живёт тут, а не
 * в двух экранах сразу: одинаковый на вид код в разных местах со временем
 * расходится, и расхождение потом ищется как ошибка.
 */
export function mediaTokens(media: MediaSummary | null | undefined): string[] {
  if (!media) return []

  const out: string[] = []
  if (media.video) out.push(media.video)

  // Подорожечно знаем только при разборе ffprobe; иначе — общий набор кодеков.
  if (media.tracks?.length) {
    for (const t of media.tracks) {
      const token = [t.codec, t.language].filter(Boolean).join(' ')
      if (token && !out.includes(token)) out.push(token)
    }
  } else if (media.audio?.length) {
    for (const c of media.audio) if (c && !out.includes(c)) out.push(c)
  }

  return out
}

export type QualityTier = '4k' | '1440' | '1080' | '720' | 'sd' | 'default'

export function qualityTier(q: number | string | null | undefined): QualityTier {
  const n = Number(q)
  if (!n || !Number.isFinite(n)) return 'default'
  if (n >= 2160) return '4k'
  if (n >= 1440) return '1440'
  if (n >= 1080) return '1080'
  if (n >= 720) return '720'
  return 'sd'
}

export function sortItems(items: TorrentItem[], sortVal: SortValue): TorrentItem[] {
  const list = items.slice()
  switch (sortVal) {
    case 'size':
      return list.sort((a, b) => (Number(b.size) || 0) - (Number(a.size) || 0))
    case 'date':
      return list.sort(
        (a, b) => toTimestamp(b.createTime) - toTimestamp(a.createTime),
      )
    case 'update':
      return list.sort(
        (a, b) => toTimestamp(b.updateTime) - toTimestamp(a.updateTime),
      )
    case 'sid':
    default:
      return list.sort((a, b) => (Number(b.sid) || 0) - (Number(a.sid) || 0))
  }
}

export function applyClientFilters(
  items: TorrentItem[],
  refine: string,
  exclude: string,
): TorrentItem[] {
  const r = refine.trim().toLowerCase()
  const e = exclude.trim().toLowerCase()
  if (!r && !e) return items
  return items.filter((el) => {
    const title = (el.title || el.name || '').toLowerCase()
    if (r && !title.includes(r)) return false
    if (e && title.includes(e)) return false
    return true
  })
}

export function buildFacets(items: TorrentItem[]) {
  const quality = new Set<string>()
  const years = new Set<string>()
  const trackers = new Set<string>()
  const voices = new Set<string>()
  const seasons = new Set<string>()
  const types = new Set<string>()

  for (const el of items) {
    if (el.quality != null && el.quality !== '') quality.add(String(el.quality))
    if (el.relased != null && el.relased !== '') years.add(String(el.relased))
    if (el.tracker) trackers.add(el.tracker)
    el.voices?.forEach((v) => voices.add(v))
    el.seasons?.forEach((s) => seasons.add(String(s)))
    el.types?.forEach((t) => types.add(t))
  }

  const sorted = (set: Set<string>) => Array.from(set).sort()

  return {
    quality: sorted(quality),
    year: sorted(years),
    tracker: sorted(trackers),
    voice: sorted(voices),
    season: sorted(seasons),
    type: sorted(types),
  }
}

export function countActiveFilters(f: SearchFilters): number {
  let n = 0
  for (const key of URL_FILTER_KEYS) {
    if (f[key]) n += 1
  }
  return n
}

export function pluralResults(n: number, locale: 'ru' | 'en' = 'ru'): string {
  const abs = Math.abs(n | 0)
  if (locale === 'en') {
    return abs === 1 ? `${n} result` : `${n} results`
  }
  const mod10 = abs % 10
  const mod100 = abs % 100
  if (mod100 >= 11 && mod100 <= 14) return `${n} результатов`
  if (mod10 === 1) return `${n} результат`
  if (mod10 >= 2 && mod10 <= 4) return `${n} результата`
  return `${n} результатов`
}

export function isSafeHttpUrl(url: string | null | undefined): boolean {
  if (!url) return false
  try {
    const u = new URL(url)
    return u.protocol === 'http:' || u.protocol === 'https:'
  } catch {
    return false
  }
}


export function torrentKey(item: TorrentItem): string {
  return [
    item.magnet,
    item.url,
    item.tracker,
    item.title || item.name,
    item.sizeName,
    item.createTime,
  ]
    .filter(Boolean)
    .join('|')
}
