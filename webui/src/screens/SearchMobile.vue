<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import FilterSheet from '@/components/FilterSheet.vue'
import Icon from '@/components/Icon.vue'
import SeedCount from '@/components/SeedCount.vue'
import { useSearch } from '@/composables/useSearch'
import { formatQualityLabel, torrentKey, type SortValue, type TorrentItem } from '@/lib/torrents'
import { isSafeMagnetUrl } from '@/lib/magnets'

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

const sheetOpen = ref(false)
const sheetTab = ref<'filters' | 'sort'>('filters')

const facets = computed(() => ({
  quality: s.facets.quality.value,
  tracker: s.facets.tracker.value,
  year: s.facets.year.value,
  voice: s.facets.voice.value,
  type: s.facets.type.value,
  size: s.facets.size.value,
}))

function meta(item: TorrentItem): string {
  const q = formatQualityLabel(item.quality)
  const hdr = String(item.videotype || '').toLowerCase() === 'hdr' ? 'HDR' : ''
  return [item.tracker, [q, hdr].filter(Boolean).join(' '), item.sizeName, item.relased]
    .filter(Boolean)
    .join(' · ')
}

function openSheet(tab: 'filters' | 'sort') {
  sheetTab.value = tab
  sheetOpen.value = true
}

function onSubmit(e: Event) {
  e.preventDefault()
  s.search()
  ;(document.activeElement as HTMLElement | null)?.blur()
}

function onPick(item: TorrentItem) {
  if (isSafeMagnetUrl(item.magnet)) window.location.href = item.magnet!
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
      <div class="relative">
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
      <li v-for="item in s.items.value" :key="torrentKey(item)">
        <button
          type="button"
          class="flex w-full flex-col gap-1.5 border-b border-g150 px-4 py-3 text-left"
          @click="onPick(item)"
        >
          <span class="text-[14px] leading-snug font-medium tracking-tight">
            {{ item.name || item.title }}
          </span>
          <span class="flex items-center justify-between gap-3">
            <span class="jb-num truncate text-[11.5px] text-g500">{{ meta(item) }}</span>
            <SeedCount :value="item.sid" :verified="item.seedersLive ?? undefined" :unknown="!!item.seedersUnknown" />
          </span>
          <span v-if="item.voices?.length" class="truncate text-[11.5px] text-g700">
            {{ item.voices.join(', ') }}
          </span>
        </button>
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
  </section>
</template>
