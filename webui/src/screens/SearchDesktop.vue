<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import Dropdown from '@/components/Dropdown.vue'
import FilterPanel from '@/components/FilterPanel.vue'
import Icon from '@/components/Icon.vue'
import TorrentCard from '@/components/TorrentCard.vue'
import TorrServerDialog from '@/components/TorrServerDialog.vue'
import { useLayout } from '@/composables/useLayout'
import { useSearch } from '@/composables/useSearch'
import { useToast } from '@/composables/useToast'
import { sendToTorrServer, TorrServerError, type TorrServerErrorCode } from '@/lib/magnets'
import { getItem, setItem, StorageKeys } from '@/lib/storage'
import { SORT_OPTIONS, torrentKey, type SortValue, type TorrentItem } from '@/lib/torrents'

/** Человеческие ответы на отказы TorrServer: код ошибки людям ничего не говорит. */
const TORR_ERRORS: Record<TorrServerErrorCode, string> = {
  invalidMagnet: 'Ссылка раздачи не подошла',
  missingUrl: 'Сначала укажите адрес TorrServer',
  unauthorized: 'TorrServer не принял логин или пароль',
  cors: 'TorrServer отказал в запросе из браузера — проверьте адрес и доступ',
  request: 'TorrServer не ответил — проверьте, что он запущен и доступен',
}

const s = useSearch()
const toast = useToast()
const { prefersCollapsedPanel } = useLayout()

/**
 * Свёрнутость панели запоминается между заходами, но на узком большом
 * экране (1024–1200) по умолчанию она свёрнута: там панель и карточки
 * уже теснят друг друга.
 */
const stored = getItem(StorageKeys.panelCollapsed)
const collapsed = ref(stored === null ? prefersCollapsedPanel.value : stored === '1')

watch(collapsed, (v) => setItem(StorageKeys.panelCollapsed, v ? '1' : '0'))

const hdrCount = computed(
  () => s.allItems.value.filter((i) => String(i.videotype || '').toLowerCase() === 'hdr').length,
)

const facets = computed(() => ({
  quality: s.facets.quality.value,
  tracker: s.facets.tracker.value,
  year: s.facets.year.value,
  voice: s.facets.voice.value,
  type: s.facets.type.value,
  size: s.facets.size.value,
}))

function onSubmit(e: Event) {
  e.preventDefault()
  s.search()
}

/**
 * Отправка в TorrServer.
 *
 * Раньше кнопка просто открывала magnet, то есть отдавала раздачу той
 * качалке, что стоит в системе — а надпись обещала TorrServer. Теперь
 * раздача уходит запросом на сам TorrServer; адрес спрашиваем в тот
 * момент, когда он впервые понадобился, и запоминаем в браузере.
 */
const torrDialog = ref(false)
const pending = ref<TorrentItem | null>(null)

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

/**
 * «В Лампе» пока заглушка.
 *
 * Кнопка открывала magnet — то есть делала совсем не то, что написано, и
 * человек получал системную качалку вместо Лампы. Пока плагина нет,
 * честнее сказать об этом прямо, чем подменять действие.
 */
function openLampa() {
  toast.info('Функция в разработке')
}

onMounted(() => s.boot())
</script>

<template>
  <div class="flex min-h-[calc(100dvh-var(--jb-header))]">
    <FilterPanel
      v-if="s.activeQuery.value"
      v-model:collapsed="collapsed"
      :filters="s.filters.value"
      :facets="facets"
      :alive="s.alive.value"
      :hdr="hdrCount"
      :active-count="s.activeCount.value"
      @toggle="s.toggle"
      @flag="s.setFlag"
      @reset="s.reset"
    />

    <div class="min-w-0 flex-1">
      <!-- Строка поиска закреплена вместе с панелью: длинный список не должен
           заставлять возвращаться наверх ради нового запроса. -->
      <form
        class="sticky z-30 flex items-center gap-2.5 border-b border-g150 bg-page px-6 py-3.5"
        style="top: var(--jb-header)"
        @submit="onSubmit"
      >
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
            placeholder="Название фильма или сериала"
            aria-label="Поисковый запрос"
            class="h-10 w-full rounded-[9px] bg-g75 pr-3 pl-9 text-[14px] outline-none placeholder:text-g500"
          />
        </div>
        <Dropdown
          :model-value="s.sort.value"
          :options="SORT_OPTIONS"
          name="Сортировка"
          @update:model-value="(v: string) => s.setSort(v as SortValue)"
        />
        <button
          type="submit"
          class="h-10 rounded-[9px] bg-ink px-5 text-[14px] font-medium text-paper disabled:opacity-50"
          :disabled="s.isLoading.value"
        >
          {{ s.isLoading.value ? 'Ищем…' : 'Найти' }}
        </button>
      </form>

      <p
        v-if="s.error.value"
        role="alert"
        class="mx-6 mb-3 rounded-lg border border-g300 px-3 py-2 text-[13px]"
      >
        {{ s.error.value }}
      </p>

      <div
        v-if="s.activeQuery.value && !s.isLoading.value"
        class="jb-num flex flex-wrap items-baseline gap-x-6 gap-y-1 px-6 pb-2.5 text-[12px] text-g500"
      >
        <span>Найдено <b class="text-[15px] text-ink">{{ s.total.value }}</b></span>
        <span>Живых <b class="text-[15px] text-ink">{{ s.alive.value }}</b></span>
        <!-- Сколько чисел подтверждено сейчас. Без этого полый контур у части
             строк выглядит необъяснимым, а числа читаются как одинаково
             надёжные — хотя непроверенное бывает годовалой давности. -->
        <span :title="'У остальных число из базы — оно может быть годовалым'">
          Проверено <b class="text-[15px] text-ink">{{ s.verified.value }}</b>
        </span>
        <span v-if="s.activeCount.value">
          Под фильтром <b class="text-[15px] text-ink">{{ s.shown.value }}</b>
        </span>
        <span v-if="s.isHuge.value">выдача крупная, отбор может подтормаживать</span>
      </div>

      <div v-if="s.isLoading.value" class="flex flex-col gap-2.5 px-6 pb-6">
        <div v-for="i in 5" :key="i" class="h-28 animate-pulse rounded-xl bg-g75" />
      </div>

      <div v-else-if="!s.activeQuery.value" class="px-6 py-10">
        <p class="text-[15px] text-g700">
          Введите название — поиск идёт по всем подключённым трекерам, сиды проверяются на месте.
        </p>
        <div v-if="s.recent.value.length" class="mt-5 flex flex-col gap-2">
          <span class="jb-label">Недавние</span>
          <div class="flex flex-wrap gap-1.5">
            <button
              v-for="q in s.recent.value"
              :key="q"
              type="button"
              class="h-7 max-w-[16rem] truncate rounded-lg bg-g75 px-2.5 text-[12.5px] text-g700 hover:text-ink"
              @click="s.search(q)"
            >
              {{ q }}
            </button>
          </div>
        </div>
      </div>

      <div
        v-else-if="!s.items.value.length"
        class="mx-6 mb-6 rounded-xl border border-dashed border-g300 px-4 py-12 text-center text-g500"
      >
        {{ s.total.value ? 'Под выбранные фильтры ничего не подошло.' : 'Ничего не нашлось.' }}
      </div>

      <div v-else class="flex flex-col gap-2.5 px-6 pb-8">
        <TorrentCard
          v-for="item in s.items.value"
          :key="torrentKey(item)"
          :item="item"
          @torr-server="openTorrServer"
          @lampa="openLampa"
        />
      </div>
    </div>

    <TorrServerDialog
      :open="torrDialog"
      @close="torrDialog = false; pending = null"
      @saved="onTorrSaved"
    />
  </div>
</template>
