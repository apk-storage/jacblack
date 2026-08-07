<script setup lang="ts">
import { computed, onUnmounted, watch } from 'vue'
import Icon from '@/components/Icon.vue'
import { SIZE_BUCKETS, type ClientFilters, type FacetCount, type FacetKey } from '@/lib/filters'
import { formatQualityLabel, SORT_OPTIONS, type SortValue } from '@/lib/torrents'

/**
 * Шторка снизу: фильтры и сортировка на телефоне.
 *
 * Снизу, а не сверху, потому что до неё дотягивается большой палец, и
 * потому что список за ней остаётся виден — понятно, что именно
 * отбирается. Кнопка внизу сразу говорит, сколько получится, чтобы не
 * применять вслепую.
 */
const props = defineProps<{
  open: boolean
  filters: ClientFilters
  facets: Record<FacetKey, FacetCount[]>
  sort: SortValue
  shown: number
  alive: number
  activeCount: number
  tab: 'filters' | 'sort'
}>()

const emit = defineEmits<{
  close: []
  toggle: [FacetKey, string]
  flag: ['hdr' | 'aliveOnly', boolean]
  sort: [SortValue]
  reset: []
  'update:tab': ['filters' | 'sort']
}>()

// Сезон первым: у сериала это главный способ сузить выдачу. У фильма сезонов
// нет вовсе, и тогда группу не показываем — на телефоне место дороже всего,
// пустой заголовок отнял бы целый ряд.
const groups = computed(() =>
  [
    { key: 'season' as const, title: 'Сезон' },
    { key: 'quality' as const, title: 'Качество' },
    { key: 'voice' as const, title: 'Дорожки · студия' },
    { key: 'size' as const, title: 'Размер' },
    { key: 'tracker' as const, title: 'Трекер' },
    { key: 'year' as const, title: 'Год' },
  ].filter((g) => g.key !== 'season' || props.facets.season.length > 0),
)

function label(key: FacetKey, value: string): string {
  if (key === 'quality') return formatQualityLabel(value) || value
  if (key === 'size') return SIZE_BUCKETS.find((b) => b.key === value)?.label ?? value
  if (key === 'season') return `${value} сезон`
  return value
}

// Пока шторка открыта, страница под ней не должна прокручиваться.
watch(
  () => props.open,
  (v) => {
    document.body.style.overflow = v ? 'hidden' : ''
  },
)

onUnmounted(() => {
  document.body.style.overflow = ''
})
</script>

<template>
  <div v-if="open" class="fixed inset-0 z-50 flex flex-col justify-end">
    <button
      type="button"
      class="absolute inset-0 bg-black/35"
      aria-label="Закрыть"
      @click="emit('close')"
    />

    <div
      class="relative flex max-h-[85dvh] flex-col gap-3 rounded-t-[18px] bg-raised px-4 pt-3.5"
      style="padding-bottom: calc(0.75rem + env(safe-area-inset-bottom))"
      role="dialog"
      aria-modal="true"
      aria-label="Фильтры и сортировка"
    >
      <span class="mx-auto block h-1 w-8 rounded-full bg-g300" aria-hidden="true" />

      <div class="flex items-center justify-between">
        <b class="text-[15px]">{{ tab === 'filters' ? 'Фильтры' : 'Сортировка' }}</b>
        <button
          v-if="activeCount"
          type="button"
          class="text-[13px] text-g500"
          @click="emit('reset')"
        >
          Сбросить
        </button>
        <button
          v-else
          type="button"
          class="flex size-7 items-center justify-center text-g500"
          aria-label="Закрыть"
          @click="emit('close')"
        >
          <Icon name="close" :size="16" />
        </button>
      </div>

      <div class="flex gap-0.5 rounded-[9px] bg-g75 p-0.5">
        <button
          v-for="t in (['filters', 'sort'] as const)"
          :key="t"
          type="button"
          class="flex-1 rounded-[7px] py-1.5 text-[12.5px]"
          :class="tab === t ? 'bg-paper font-medium text-ink shadow-sm' : 'text-g500'"
          @click="emit('update:tab', t)"
        >
          {{ t === 'filters' ? 'Фильтры' : 'Сортировка' }}
        </button>
      </div>

      <div class="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto pb-2">
        <template v-if="tab === 'filters'">
          <!--
            Живость особняком и без заголовка группы: это свойство раздачи
            на трекере, а не свойство файла, и в один ряд с качеством
            или HDR она не встаёт.
          -->
          <button
            type="button"
            class="inline-flex h-7 shrink-0 items-center gap-1.5 self-start rounded-[7px] px-2.5 text-[12.5px]"
            :class="filters.aliveOnly ? 'bg-ink text-paper' : 'bg-g75 text-g700'"
            @click="emit('flag', 'aliveOnly', !filters.aliveOnly)"
          >
            только живые
            <span class="jb-num text-[11px] opacity-70">{{ alive }}</span>
          </button>

          <div v-for="g in groups" :key="g.key" class="flex flex-col gap-2">
            <span class="jb-label">{{ g.title }}</span>
            <div v-if="facets[g.key].length" class="flex flex-wrap gap-1.5">
              <!-- HDR — свойство картинки, поэтому живёт внутри качества -->
              <button
                v-if="g.key === 'quality'"
                type="button"
                class="inline-flex h-7 items-center rounded-[7px] px-2.5 text-[12.5px]"
                :class="filters.hdr ? 'bg-ink text-paper' : 'bg-g75 text-g700'"
                @click="emit('flag', 'hdr', !filters.hdr)"
              >
                HDR
              </button>
              <button
                v-for="row in facets[g.key]"
                :key="row.value"
                type="button"
                class="inline-flex h-7 items-center gap-1.5 rounded-[7px] px-2.5 text-[12.5px]"
                :class="[
                  filters[g.key].includes(row.value) ? 'bg-ink text-paper' : 'bg-g75 text-g700',
                  row.count === 0 ? 'opacity-40' : '',
                ]"
                @click="emit('toggle', g.key, row.value)"
              >
                {{ label(g.key, row.value) }}
                <span class="jb-num text-[11px] opacity-70">{{ row.count }}</span>
              </button>
            </div>
            <span v-else class="text-[12px] text-g300">нет данных</span>
          </div>
        </template>

        <div v-else class="flex flex-col">
          <button
            v-for="o in SORT_OPTIONS"
            :key="o.value"
            type="button"
            class="flex items-center justify-between border-b border-g150 py-3 text-left text-[14px]"
            :class="sort === o.value ? 'text-ink' : 'text-g700'"
            @click="emit('sort', o.value)"
          >
            {{ o.label }}
            <Icon v-if="sort === o.value" name="check" :size="16" />
          </button>
        </div>
      </div>

      <button
        type="button"
        class="h-11 shrink-0 rounded-[11px] bg-ink text-[14.5px] font-semibold text-paper"
        @click="emit('close')"
      >
        Показать {{ shown }}
      </button>
    </div>
  </div>
</template>
