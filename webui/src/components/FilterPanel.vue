<script setup lang="ts">
import { computed, ref } from 'vue'
import Icon from '@/components/Icon.vue'
import { SIZE_BUCKETS, type FacetKey, type ClientFilters, type FacetCount } from '@/lib/filters'
import { formatQualityLabel } from '@/lib/torrents'

/**
 * Панель фильтров большого экрана.
 *
 * Закреплена и прокручивается сама: список выдачи длинный, и возвращаться
 * к его началу, чтобы снять фильтр, — работа, которой быть не должно.
 *
 * Порядок групп не случаен: качество первым, потому что выбирают прежде
 * всего по нему. Дальше дорожки, размер, трекер, год.
 *
 * Значения, под которые ничего не попадает, гаснут, но остаются на месте:
 * так видно, что фильтр существует и он пуст, а не что он куда-то исчез.
 */
const props = defineProps<{
  filters: ClientFilters
  facets: Record<FacetKey, FacetCount[]>
  alive: number
  hdr: number
  collapsed: boolean
  activeCount: number
}>()

const emit = defineEmits<{
  toggle: [FacetKey, string]
  flag: ['hdr' | 'aliveOnly', boolean]
  reset: []
  'update:collapsed': [boolean]
}>()

const LIMIT = 6

/** Какие группы человек раскрыл целиком. */
const expanded = ref<Partial<Record<FacetKey, boolean>>>({})

function label(key: FacetKey, value: string): string {
  if (key === 'quality') return formatQualityLabel(value) || value
  if (key === 'size') return SIZE_BUCKETS.find((b) => b.key === value)?.label ?? value
  return value
}

const groups = [
  { key: 'quality' as const, title: 'Качество' },
  { key: 'voice' as const, title: 'Дорожки · студия' },
  { key: 'size' as const, title: 'Размер' },
  { key: 'tracker' as const, title: 'Трекер' },
  { key: 'year' as const, title: 'Год' },
  { key: 'type' as const, title: 'Тип' },
]

function rows(key: FacetKey): FacetCount[] {
  const all = props.facets[key]
  return expanded.value[key] ? all : all.slice(0, LIMIT)
}

const groupActive = computed(() => groups.map((g) => props.filters[g.key].length > 0))
</script>

<template>
  <aside
    v-if="collapsed"
    class="sticky flex w-14 shrink-0 flex-col items-center gap-3 self-start border-r border-g150 py-4"
    style="top: var(--jb-header); height: calc(100dvh - var(--jb-header))"
  >
    <button
      type="button"
      class="relative flex size-8 items-center justify-center rounded-lg bg-g75 text-g700 hover:text-ink"
      aria-label="Развернуть фильтры"
      @click="emit('update:collapsed', false)"
    >
      <Icon name="filter" :size="16" />
      <span
        v-if="activeCount"
        class="jb-num absolute -top-1.5 -right-1.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-ink px-1 text-[10px] text-paper"
      >
        {{ activeCount }}
      </span>
    </button>
    <span
      v-for="(on, i) in groupActive"
      :key="i"
      class="size-[7px] rounded-full"
      :class="on ? 'bg-ink' : 'bg-g300'"
      aria-hidden="true"
    />
  </aside>

  <aside
    v-else
    class="sticky flex w-62 shrink-0 flex-col self-start border-r border-g150"
    style="top: var(--jb-header); height: calc(100dvh - var(--jb-header))"
  >
    <div class="flex items-center justify-between px-4 py-3.5">
      <b class="text-[13.5px]">Фильтры</b>
      <div class="flex items-center gap-1">
        <button
          v-if="activeCount"
          type="button"
          class="rounded-md px-1.5 py-0.5 text-[12px] text-g500 hover:text-ink"
          @click="emit('reset')"
        >
          Сбросить
        </button>
        <button
          type="button"
          class="flex size-6.5 items-center justify-center rounded-lg border border-g150 text-g500 hover:text-ink"
          aria-label="Свернуть фильтры"
          @click="emit('update:collapsed', true)"
        >
          <Icon name="chevronLeft" :size="13" />
        </button>
      </div>
    </div>

    <div class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-4 pb-6">
      <!--
        Живость стоит особняком и без заголовка группы: это свойство раздачи
        на трекере, а не свойство файла, и в один ряд с качеством или HDR
        она не встаёт.
      -->
      <label class="flex cursor-pointer items-center gap-2.5 text-[13px] text-g700">
        <input
          type="checkbox"
          class="size-3.5 accent-ink"
          :checked="filters.aliveOnly"
          @change="emit('flag', 'aliveOnly', ($event.target as HTMLInputElement).checked)"
        />
        только живые
        <span class="jb-num ml-auto text-[11.5px] text-g500">{{ alive }}</span>
      </label>

      <div v-for="g in groups" :key="g.key" class="flex flex-col gap-1.5">
        <span class="jb-label">{{ g.title }}</span>

        <!-- HDR — свойство картинки, поэтому живёт внутри качества -->
        <label
          v-if="g.key === 'quality'"
          class="flex cursor-pointer items-center gap-2.5 text-[13px]"
          :class="hdr === 0 ? 'text-g300' : 'text-g700'"
        >
          <input
            type="checkbox"
            class="size-3.5 accent-ink"
            :checked="filters.hdr"
            @change="emit('flag', 'hdr', ($event.target as HTMLInputElement).checked)"
          />
          HDR
          <span class="jb-num ml-auto text-[11.5px]" :class="hdr === 0 ? 'text-g300' : 'text-g500'">
            {{ hdr }}
          </span>
        </label>

        <label
          v-for="row in rows(g.key)"
          :key="row.value"
          class="flex cursor-pointer items-center gap-2.5 text-[13px]"
          :class="row.count === 0 ? 'text-g300' : 'text-g700'"
        >
          <input
            type="checkbox"
            class="size-3.5 accent-ink"
            :checked="filters[g.key].includes(row.value)"
            @change="emit('toggle', g.key, row.value)"
          />
          <span class="truncate" :title="label(g.key, row.value)">{{ label(g.key, row.value) }}</span>
          <span class="jb-num ml-auto text-[11.5px]" :class="row.count === 0 ? 'text-g300' : 'text-g500'">
            {{ row.count }}
          </span>
        </label>

        <button
          v-if="facets[g.key].length > LIMIT"
          type="button"
          class="self-start text-[12px] text-g500 underline-offset-2 hover:text-ink hover:underline"
          @click="expanded[g.key] = !expanded[g.key]"
        >
          {{ expanded[g.key] ? 'свернуть' : `ещё ${facets[g.key].length - LIMIT}` }}
        </button>

        <span v-if="!facets[g.key].length" class="text-[12px] text-g300">нет данных</span>
      </div>
    </div>
  </aside>
</template>
