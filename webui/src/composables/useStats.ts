import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { apiClient } from '@/lib/api/client'
import {
  aggregateTrackers,
  formatStatsUpdatedAt,
  type StatsCrawl,
  type StatsMeta,
  type StatsQuality,
  type StatsTrackers,
  type TrackerFreshness,
  type TrackerStat,
} from '@/lib/stats'

/**
 * Статистика по трекерам. Две точки: список трекеров и время последнего
 * пересчёта.
 *
 * Обновляется редко — сама сводка считается на сервере по расписанию,
 * поэтому дёргать её чаще раза в пять минут смысла нет.
 */
export function useStats() {
  const trackers = useQuery({
    queryKey: ['stats', 'torrents'],
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      const data = await apiClient.getStatsTorrents({ signal, timeoutMs: 20_000 })
      return Array.isArray(data) ? (data as TrackerStat[]) : []
    },
  })

  const meta = useQuery({
    queryKey: ['stats', 'meta'],
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      return (await apiClient.getStatsMeta({ signal, timeoutMs: 10_000 })) as StatsMeta
    },
  })

  /**
   * Чему верить в выдаче: доля живых чисел, опознанные удалённые раздачи,
   * полнота словаря кодов. Меняется медленно, дёргаем редко.
   */
  const quality = useQuery({
    queryKey: ['stats', 'quality'],
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      return (await apiClient.getStatsQuality({ signal, timeoutMs: 10_000 })) as StatsQuality
    },
  })

  /**
   * Давно ли каждый источник выкладывал новое.
   *
   * Отдельно от объёмов потому, что отвечает на другой вопрос. Колонка
   * «Последняя» говорит, когда МЫ добавили запись, а это — что есть у самого
   * источника. Разница не теоретическая: animetosho три месяца отвечал кодом
   * 200 и отдавал майские данные, и по нашим числам это выглядело нормально.
   */
  const freshness = useQuery({
    queryKey: ['stats', 'trackers'],
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => {
      const data = (await apiClient.getStatsTrackers({ signal, timeoutMs: 10_000 })) as StatsTrackers
      return data?.trackers ?? []
    },
  })

  /**
   * Ход глубоких обходов. Единственное здесь, что меняется на глазах, —
   * поэтому и обновляется чаще остального, раз в минуту.
   */
  const crawl = useQuery({
    queryKey: ['stats', 'crawl'],
    staleTime: 60_000,
    refetchInterval: 60_000,
    queryFn: async ({ signal }) => {
      return (await apiClient.getStatsCrawl({ signal, timeoutMs: 10_000 })) as StatsCrawl
    },
  })

  /** По имени источника — чтобы таблица брала своё за один просмотр. */
  const freshnessByTracker = computed(() => {
    const map = new Map<string, TrackerFreshness>()
    for (const f of freshness.data.value ?? []) {
      if (f?.tracker) map.set(f.tracker.toLowerCase(), f)
    }
    return map
  })

  const list = computed(() => trackers.data.value ?? [])

  return {
    list,
    /** Отсортировано по числу раздач: крупные источники сверху. */
    sorted: computed(() =>
      list.value
        .slice()
        .sort((a, b) => (Number(b.alltorrents) || 0) - (Number(a.alltorrents) || 0)),
    ),
    total: computed(() => aggregateTrackers(list.value)),
    updatedAt: computed(() => formatStatsUpdatedAt(meta.data.value?.updatedAtLocal ?? meta.data.value?.updatedAt)),
    isLoading: computed(() => trackers.isLoading.value),
    quality: computed(() => quality.data.value ?? null),
    freshnessOf: (trackerName?: string | null): TrackerFreshness | undefined =>
      trackerName ? freshnessByTracker.value.get(trackerName.toLowerCase()) : undefined,
    /** Обходы, идущие прямо сейчас. */
    crawlRunning: computed(() => (crawl.data.value?.runs ?? []).filter((r) => r.running)),
    /** Последние завершившиеся — по ним видно, обход прошёл или оборвался. */
    crawlFinished: computed(() =>
      (crawl.data.value?.runs ?? []).filter((r) => !r.running).slice(0, 6),
    ),
    /** Непустые очереди и закладки возобновляемых обходов. */
    crawlQueues: computed(() =>
      (crawl.data.value?.queues ?? [])
        .filter((q) => q.value > 0)
        .sort((a, b) => b.value - a.value),
    ),
    /** Кто молчит дольше недели — то, что стоит показать отдельно. */
    silent: computed(() =>
      (freshness.data.value ?? [])
        .filter((f) => (f.silentDays ?? 0) > 7)
        .sort((a, b) => (b.silentDays ?? 0) - (a.silentDays ?? 0)),
    ),
    error: computed(() => (trackers.isError.value ? 'Статистика не загрузилась.' : '')),
  }
}
