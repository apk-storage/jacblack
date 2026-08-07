<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import FilterSheet from '@/components/FilterSheet.vue'
import Icon from '@/components/Icon.vue'
import SeedCount from '@/components/SeedCount.vue'
import TorrServerDialog from '@/components/TorrServerDialog.vue'
import { useSearch } from '@/composables/useSearch'
import { useToast } from '@/composables/useToast'
import {
  formatQualityLabel,
  formatDate,
  isSafeHttpUrl,
  torrentKey,
  type SortValue,
  type TorrentItem,
} from '@/lib/torrents'
import { isSafeMagnetUrl, sendToTorrServer, TorrServerError, type TorrServerErrorCode } from '@/lib/magnets'
import { getItem, StorageKeys } from '@/lib/storage'

/** Человеческие ответы на отказы TorrServer. */
const TORR_ERRORS: Record<TorrServerErrorCode, string> = {
  invalidMagnet: 'Ссылка раздачи не подошла',
  missingUrl: 'Сначала укажите адрес TorrServer',
  unauthorized: 'TorrServer не принял логин или пароль',
  cors: 'TorrServer отказал в запросе',
  request: 'TorrServer не ответил — проверьте адрес и что он запущен',
}

/**
 * Поиск на телефоне: строки вместо карточек.
 *
 * Карточка с подписанной строкой фактов на 320 пикселях превращается
 * в лесенку, поэтому здесь другое устройство: название, под ним строка
 * машинных данных, сиды прижаты вправо — по ним и выбирают.
 *
 * Виртуализации нет намеренно. Прежний интерфейс держал её ради длинных
 * списков и платил за это без малого четырьмя сотнями строк борьбы
 * прокрутки с липкой шапкой на iOS. Типичная выдача — сотни строк,
 * обычный список с ними справляется.
 */
const s = useSearch()
const toast = useToast()

const sheetOpen = ref(false)
const sheetTab = ref<'filters' | 'sort'>('filters')
const torrDialog = ref(false)
const pending = ref<TorrentItem | null>(null)

const facets = computed(() => ({
  quality: s.facets.quality.value,
  tracker: s.facets.tracker.value,
  year: s.facets.year.value,
  voice: s.facets.voice.value,
  type: s.facets.type.value,
  size: s.facets.size.value,
}))


function openSheet(tab: 'filters' | 'sort') {
  sheetTab.value = tab
  sheetOpen.value = true
}

function onSubmit(e: Event) {
  e.preventDefault()
  s.search()
  ;(document.activeElement as HTMLElement | null)?.blur()
}

function seasonLabel(item: TorrentItem): string {
  const s = (item.seasons ?? []).map(String).filter(Boolean)
  if (!s.length) return ''
  return s.length === 1 ? `Сезон ${s[0]}` : `Сезоны ${s.join(', ')}`
}

function chips(item: TorrentItem): string[] {
  const q = formatQualityLabel(item.quality)
  const hdr = String(item.videotype || '').toLowerCase() === 'hdr' ? 'HDR' : ''
  const quality = [q, hdr].filter(Boolean).join(' ')
  return [
    item.tracker,
    quality,
    seasonLabel(item),
    item.sizeName,
    item.relased || formatDate(item.createTime),
  ].filter(Boolean) as string[]
}

function pageUrl(item: TorrentItem): string {
  return isSafeHttpUrl(item.url) ? item.url! : ''
}
function magnetOf(item: TorrentItem): string {
  return isSafeMagnetUrl(item.magnet) ? item.magnet! : ''
}

async function sendPending(item: TorrentItem) {
  const magnet = (item.magnet || '').trim()
  if (!magnet) {
    toast.error('У этой раздачи нет magnet-ссылки')
    return
  }
  try {
    await sendToTorrServer(magnet, {
      baseUrl: getItem(StorageKeys.torrServerUrl) ?? '',
      login: getItem(StorageKeys.torrServerLogin) ?? '',
      password: getItem(StorageKeys.torrServerPassword) ?? '',
    })
    toast.success('Раздача добавлена в TorrServer')
  } catch (e) {
    const code = e instanceof TorrServerError ? e.code : 'request'
    toast.error(TORR_ERRORS[code] ?? TORR_ERRORS.request)
  }
}

function openTorrServer(item: TorrentItem) {
  if (!(getItem(StorageKeys.torrServerUrl) ?? '').trim()) {
    pending.value = item
    torrDialog.value = true
    return
  }
  void sendPending(item)
}

function onTorrSaved() {
  torrDialog.value = false
  const item = pending.value
  pending.value = null
  if (item) void sendPending(item)
}

onMounted(() => s.boot())
</script>

<template>
  <section class="flex flex-col">
    <form
      class="sticky z-30 flex flex-col gap-2 border-b border-g150 bg-paper px-4 py-2.5"
      style="top: var(--jb-header)"
      @submit="onSubmit"
    >
      <div class="flex items-center gap-2">
        <div class="relative min-w-0 flex-1">
          <Icon
            name="search"
            :size="15"
            class="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-g500"
          />
          <input
            v-model="s.query.value"
            type="search"
            enterkeyhint="search"
            autocomplete="off"
            placeholder="Название"
            aria-label="Поисковый запрос"
            class="h-10 w-full rounded-[9px] bg-g75 pr-3 pl-9 text-[15px] outline-none placeholder:text-g500"
          />
        </div>
        <button
          type="button"
          class="flex h-10 w-10 shrink-0 items-center justify-center rounded-[9px] bg-g75 text-g500"
          aria-label="Настройки TorrServer"
          @click="torrDialog = true"
        >
          <Icon name="server" :size="17" />
        </button>
      </div>

      <div v-if="s.activeQuery.value" class="flex gap-1.5 overflow-x-auto">
        <button
          type="button"
          class="inline-flex h-7 shrink-0 items-center gap-1.5 rounded-[7px] px-2.5 text-[12.5px]"
          :class="s.activeCount.value ? 'bg-ink text-paper' : 'bg-g75 text-g700'"
          @click="openSheet('filters')"
        >
          <Icon name="filter" :size="13" />
          Фильтры
          <span v-if="s.activeCount.value" class="jb-num text-[11px]">{{ s.activeCount.value }}</span>
        </button>
        <button
          type="button"
          class="inline-flex h-7 shrink-0 items-center gap-1.5 rounded-[7px] bg-g75 px-2.5 text-[12.5px] text-g700"
          @click="openSheet('sort')"
        >
          <Icon name="sort" :size="13" />
          Сортировка
        </button>
      </div>
    </form>

    <p v-if="s.error.value" role="alert" class="mx-4 mt-3 rounded-lg border border-g300 px-3 py-2 text-[13px]">
      {{ s.error.value }}
    </p>

    <div
      v-if="s.activeQuery.value && !s.isLoading.value"
      class="jb-num flex items-baseline gap-5 px-4 py-2 text-[11.5px] text-g500"
    >
      <span>Найдено <b class="text-[14px] text-ink">{{ s.total.value }}</b></span>
      <span>Живых <b class="text-[14px] text-ink">{{ s.alive.value }}</b></span>
      <span v-if="s.activeCount.value">Показано <b class="text-[14px] text-ink">{{ s.shown.value }}</b></span>
    </div>

    <div v-if="s.isLoading.value" class="flex flex-col gap-px px-4 py-2">
      <div v-for="i in 8" :key="i" class="h-14 animate-pulse rounded-lg bg-g75" />
    </div>

    <div v-else-if="!s.activeQuery.value" class="flex flex-col gap-4 px-4 py-8">
      <p class="text-[14px] text-g700">Введите название — сиды проверяются на месте.</p>
      <div v-if="s.recent.value.length" class="flex flex-col gap-1">
        <span class="jb-label pb-1">Недавние</span>
        <button
          v-for="q in s.recent.value"
          :key="q"
          type="button"
          class="flex items-center gap-2 border-b border-g150 py-2.5 text-left text-[14px] text-g700"
          @click="s.search(q)"
        >
          <Icon name="search" :size="14" class="shrink-0 text-g500" />
          <span class="truncate">{{ q }}</span>
        </button>
      </div>
    </div>

    <div
      v-else-if="!s.items.value.length"
      class="mx-4 my-6 rounded-xl border border-dashed border-g300 px-4 py-10 text-center text-[13px] text-g500"
    >
      {{ s.total.value ? 'Под выбранные фильтры ничего не подошло.' : 'Ничего не нашлось.' }}
    </div>

    <ul v-else class="flex flex-col">
      <li
        v-for="item in s.items.value"
        :key="torrentKey(item)"
        class="flex flex-col gap-1.5 border-b border-g150 px-4 py-3"
      >
        <p class="text-[14px] leading-snug font-medium tracking-tight">
          {{ item.name || item.title }}
        </p>

        <div class="jb-num flex flex-wrap items-center gap-x-2 gap-y-1 text-[11.5px] text-g500">
          <span v-for="(c, i) in chips(item)" :key="i">
            <span v-if="i" class="mr-2 text-g300">·</span>{{ c }}
          </span>
        </div>

        <p v-if="item.voices?.length" class="text-[11.5px] leading-snug text-g700">
          {{ item.voices.join(', ') }}
        </p>

        <div class="mt-0.5 flex flex-wrap items-center gap-1.5">
          <SeedCount
            :value="item.sid"
            :verified="item.seedersLive ?? undefined"
            :unknown="!!item.seedersUnknown"
          />
          <span class="flex-1"></span>
          <a
            v-if="pageUrl(item)"
            :href="pageUrl(item)"
            target="_blank"
            rel="noopener noreferrer"
            class="inline-flex h-8 items-center gap-1 rounded-lg bg-g75 px-2.5 text-[12px] text-g700 no-underline"
          >
            <Icon name="external" :size="13" /> Трекер
          </a>
          <button
            v-if="magnetOf(item)"
            type="button"
            class="inline-flex h-8 items-center gap-1 rounded-lg bg-g75 px-2.5 text-[12px] text-g700"
            @click="openTorrServer(item)"
          >
            <Icon name="server" :size="13" /> TorrServer
          </button>
          <a
            v-if="magnetOf(item)"
            :href="magnetOf(item)"
            class="inline-flex h-8 items-center gap-1 rounded-lg bg-g75 px-2.5 text-[12px] text-g700 no-underline"
          >
            <Icon name="magnet" :size="13" /> Открыть
          </a>
        </div>
      </li>
    </ul>

    <FilterSheet
      v-model:tab="sheetTab"
      :open="sheetOpen"
      :filters="s.filters.value"
      :facets="facets"
      :sort="s.sort.value"
      :shown="s.shown.value"
      :alive="s.alive.value"
      :active-count="s.activeCount.value"
      @close="sheetOpen = false"
      @toggle="s.toggle"
      @flag="s.setFlag"
      @sort="(v: SortValue) => s.setSort(v)"
      @reset="s.reset"
    />

    <TorrServerDialog
      :open="torrDialog"
      @close="torrDialog = false; pending = null"
      @saved="onTorrSaved"
    />
  </section>
</template>
