<script setup lang="ts">
import { computed, ref } from 'vue'
import Icon from '@/components/Icon.vue'
import SeedCount from '@/components/SeedCount.vue'
import { formatQualityLabel, formatDate, isSafeHttpUrl, type TorrentItem } from '@/lib/torrents'
import { isSafeMagnetUrl } from '@/lib/magnets'

const props = defineProps<{ item: TorrentItem }>()
const emit = defineEmits<{ torrServer: [TorrentItem]; lampa: [TorrentItem] }>()

const rawOpen = ref(false)

/**
 * Заголовок разбираем надвое.
 *
 * Трекеры отдают строки по двести с лишним символов, где к названию
 * приклеены режиссёр, страна, жанры, кодеки и перечень дорожек. Читать это
 * невозможно, а выбросить нельзя: там единственные сведения об источнике
 * (BDRemux против WEB-DL), релиз-группе и составе звука — отдельными полями
 * они не хранятся. Поэтому название крупно, исходная строка мелко и
 * в одну строку, с раскрытием по нажатию.
 */
const name = computed(() => {
  const n = (props.item.name || '').trim()
  const o = (props.item.originalname || '').trim()
  if (n && o && o.toLowerCase() !== n.toLowerCase()) return `${n} / ${o}`
  return n || o || (props.item.title || '').trim()
})

const raw = computed(() => (props.item.title || '').trim())
const showRaw = computed(() => raw.value && raw.value !== name.value)

const quality = computed(() => {
  const q = formatQualityLabel(props.item.quality)
  const hdr = String(props.item.videotype || '').toLowerCase() === 'hdr'
  return [q, hdr ? 'HDR' : ''].filter(Boolean).join(' ')
})

const voices = computed(() => props.item.voices?.filter(Boolean) ?? [])

/** Сводка приходит с сервера; пустую он не присылает вовсе. */
const media = computed(() => props.item.media ?? null)

/**
 * Ссылка на IMDb — только если код похож на настоящий.
 *
 * Проверяем форму, а не доверяем полю: код приходит из разбора страниц
 * трекеров, и подставить в адрес что угодно нельзя.
 */
const imdbUrl = computed(() => {
  const code = String(props.item.imdb || '').trim()
  return /^tt\d{6,}$/i.test(code) ? `https://www.imdb.com/title/${code}/` : null
})

/**
 * Каналы показываем привычной записью: 6 → 5.1, 8 → 7.1, 2 → 2.0.
 * Число каналов само по себе человеку ничего не говорит.
 */
function channelsLabel(channels?: number | null): string {
  if (!channels || channels < 1) return ''
  if (channels === 1) return 'mono'
  if (channels === 2) return '2.0'
  return `${channels - 1}.1`
}
const pageUrl = computed(() => (isSafeHttpUrl(props.item.url) ? props.item.url! : ''))
const magnet = computed(() => (isSafeMagnetUrl(props.item.magnet) ? props.item.magnet! : ''))
</script>

<template>
  <article class="flex flex-col gap-2.5 rounded-xl border border-g150 bg-raised px-4 py-3">
    <div class="flex flex-col gap-1">
      <h3 class="text-[14.5px] leading-snug font-medium tracking-tight">{{ name }}</h3>
      <p
        v-if="showRaw"
        class="cursor-pointer text-xs leading-snug text-g500"
        :class="rawOpen ? '' : 'truncate'"
        :title="rawOpen ? '' : 'Показать целиком'"
        @click="rawOpen = !rawOpen"
      >
        {{ raw }}
      </p>
    </div>

    <div class="flex flex-wrap gap-x-5 gap-y-1.5">
      <div class="flex flex-col">
        <span class="jb-label">Трекер</span>
        <span class="jb-num text-[13px]">{{ item.tracker || '—' }}</span>
      </div>
      <div v-if="quality" class="flex flex-col">
        <span class="jb-label">Качество</span>
        <span class="jb-num text-[13px]">{{ quality }}</span>
      </div>
      <div class="flex flex-col">
        <span class="jb-label">Размер</span>
        <span class="jb-num text-[13px]">{{ item.sizeName || '—' }}</span>
      </div>
      <div class="flex flex-col">
        <span class="jb-label">Сиды</span>
        <SeedCount :value="item.sid" :verified="item.seedersLive ?? undefined" :unknown="!!item.seedersUnknown" />
      </div>
      <div v-if="item.relased" class="flex flex-col">
        <span class="jb-label">Год</span>
        <span class="jb-num text-[13px]">{{ item.relased }}</span>
      </div>
      <div class="flex flex-col">
        <span class="jb-label">Добавлено</span>
        <span class="jb-num text-[13px]">{{ formatDate(item.createTime) }}</span>
      </div>
    </div>

    <!--
      Дорожки — своя строка внизу, а не ещё одна колонка справа: это список,
      а всё остальное в строке фактов — одиночные значения.

      Студия словом, кодеки и языки плашками. Пустых значений не показываем
      вовсе: у большинства раздач нет разбора ffprobe, и прочерки вместо
      данных занимали бы место, ничего не сообщая.
    -->
    <div
      v-if="voices.length || media"
      class="flex flex-wrap items-baseline gap-x-2.5 gap-y-1 border-t border-g150 pt-2"
    >
      <span class="jb-label shrink-0">Дорожки</span>

      <span v-if="voices.length" class="text-[12.5px] text-g700">{{ voices.join(', ') }}</span>

      <span v-if="media?.video" class="jb-token">{{ media.video }}</span>

      <!-- Дорожки по одной знаем только из ffprobe; иначе — общий набор кодеков. -->
      <template v-if="media?.tracks?.length">
        <span v-for="(t, i) in media.tracks" :key="i" class="jb-token">
          {{ [t.codec, t.language, channelsLabel(t.channels)].filter(Boolean).join(' ') }}
        </span>
      </template>
      <template v-else-if="media?.audio?.length">
        <span v-for="c in media.audio" :key="c" class="jb-token">{{ c }}</span>
      </template>

      <span v-if="media?.subtitles?.length" class="text-[12px] text-g500">
        суб: {{ media.subtitles.join(', ') }}
      </span>
    </div>

    <div class="flex flex-wrap items-center gap-1.5">
      <a
        v-if="pageUrl"
        :href="pageUrl"
        target="_blank"
        rel="noopener noreferrer"
        class="inline-flex h-7 items-center gap-1.5 rounded-lg bg-g75 px-2.5 text-[12.5px] text-g700 no-underline hover:text-ink"
      >
        <Icon name="external" :size="13" />
        На трекере
      </a>
      <!-- Код IMDB знаем у 40% базы, и он единственное, чем разводятся
           тёзки. Показываем ссылкой: человеку это проверка, что найдена
           именно та вещь, а не одноимённая. -->
      <a
        v-if="imdbUrl"
        :href="imdbUrl"
        target="_blank"
        rel="noopener noreferrer"
        class="inline-flex h-7 items-center gap-1.5 rounded-lg bg-g75 px-2.5 text-[12.5px] text-g700 no-underline hover:text-ink"
        :title="`Карточка ${item.imdb} на IMDb`"
      >
        <Icon name="external" :size="13" />
        IMDb
      </a>
      <button
        v-if="magnet"
        type="button"
        class="inline-flex h-7 items-center gap-1.5 rounded-lg bg-g75 px-2.5 text-[12.5px] text-g700 hover:text-ink"
        @click="emit('torrServer', item)"
      >
        <Icon name="server" :size="13" />
        В TorrServer
      </button>
      <a
        v-if="magnet"
        :href="magnet"
        class="inline-flex h-7 items-center gap-1.5 rounded-lg bg-g75 px-2.5 text-[12.5px] text-g700 no-underline hover:text-ink"
      >
        <Icon name="magnet" :size="13" />
        Magnet
      </a>
      <button
        v-if="magnet"
        type="button"
        class="inline-flex h-7 items-center gap-1.5 rounded-lg bg-g75 px-2.5 text-[12.5px] text-g700 hover:text-ink"
        @click="emit('lampa', item)"
      >
        <Icon name="play" :size="13" />
        В Лампе
      </button>
    </div>
  </article>
</template>
