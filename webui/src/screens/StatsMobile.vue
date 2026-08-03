<script setup lang="ts">
import { useStats } from '@/composables/useStats'
import {
  formatDuration,
  formatSilence,
  formatStatNumberFull,
  getTrackerDisplayName,
  getTracksData,
} from '@/lib/stats'

/**
 * Статистика на телефоне: строки вместо таблицы.
 *
 * Семь колонок на 320 пикселях не живут, поэтому на строку выносим два
 * числа, по которым и смотрят, — всего раздач и новых за сутки, — а
 * остальное убираем во вторую строку помельче.
 */
const s = useStats()
</script>

<template>
  <section class="flex flex-col">
    <header class="flex flex-col gap-0.5 px-4 pt-5 pb-3">
      <h1 class="text-xl font-semibold tracking-tight">Статистика</h1>
      <span v-if="s.updatedAt.value" class="jb-num text-[11.5px] text-g500">
        пересчитано {{ s.updatedAt.value }}
      </span>
    </header>

    <p v-if="s.error.value" role="alert" class="mx-4 rounded-lg border border-g300 px-3 py-2 text-[13px]">
      {{ s.error.value }}
    </p>

    <div v-if="s.isLoading.value" class="flex flex-col gap-2 px-4">
      <div v-for="i in 8" :key="i" class="h-12 animate-pulse rounded-lg bg-g75" />
    </div>

    <template v-else>
      <div class="grid grid-cols-2 gap-3 px-4 pb-4">
        <div class="flex flex-col rounded-xl border border-g150 px-3 py-2.5">
          <span class="jb-label">Всего раздач</span>
          <span class="jb-num text-xl font-semibold">
            {{ formatStatNumberFull(s.total.value.alltorrents) }}
          </span>
        </div>
        <div class="flex flex-col rounded-xl border border-g150 px-3 py-2.5">
          <span class="jb-label">Новых за сутки</span>
          <span class="jb-num text-xl font-semibold">
            {{ formatStatNumberFull(s.total.value.newtor) }}
          </span>
        </div>
      </div>

      <!-- На телефоне из всей сводки по обходам оставляем одно: что идёт
           прямо сейчас. Очереди и прошлые заходы там не читают. -->
      <p
        v-if="s.crawlRunning.value.length"
        class="mx-4 mb-4 rounded-xl bg-g75 px-3 py-2 text-[12px] text-g700"
      >
        <span class="text-g500">Идёт обход:</span>
        <template v-for="(r, i) in s.crawlRunning.value" :key="r.tracker">
          <template v-if="i">, </template>
          <b class="text-ink">{{ getTrackerDisplayName(r.tracker) }}</b>
          — {{ formatDuration(r.minutes) }}
        </template>
      </p>

      <ul class="flex flex-col">
        <li
          v-for="t in s.sorted.value"
          :key="t.trackerName || ''"
          class="flex flex-col gap-1 border-b border-g150 px-4 py-3"
        >
          <div class="flex items-baseline justify-between gap-3">
            <span class="text-[14px] font-medium">{{ getTrackerDisplayName(t.trackerName) }}</span>
            <span class="jb-num text-[14px]">{{ formatStatNumberFull(t.alltorrents) }}</span>
          </div>
          <div class="jb-num flex items-baseline gap-4 text-[11.5px] text-g500">
            <span>новых {{ formatStatNumberFull(t.newtor) }}</span>
            <span>дорожки {{ formatStatNumberFull(getTracksData(t).confirm) }}</span>
            <!-- На телефоне из двух дат оставляем ту, что важнее: сколько
                 источник молчит. Наша дата записи там всё равно не читается. -->
            <span
              class="ml-auto"
              :class="formatSilence(s.freshnessOf(t.trackerName)?.silentDays).alarming
                ? 'font-medium text-ink'
                : ''"
            >
              {{ formatSilence(s.freshnessOf(t.trackerName)?.silentDays).text }}
            </span>
          </div>
        </li>
      </ul>
    </template>
  </section>
</template>
