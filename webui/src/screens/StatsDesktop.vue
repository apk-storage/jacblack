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
 * Статистика по источникам.
 *
 * Таблица, а не карточки: двадцать строк с одинаковым набором чисел
 * сравнивают по колонкам, и любое другое устройство тут только мешает.
 * Числа моноширинные и выровнены вправо — иначе разряды не совпадают
 * и глазу не за что зацепиться.
 */
const s = useStats()
</script>

<template>
  <section class="flex flex-col gap-5 px-6 py-6">
    <header class="flex flex-wrap items-baseline justify-between gap-3">
      <h1 class="text-2xl font-semibold tracking-tight">Статистика</h1>
      <span v-if="s.updatedAt.value" class="jb-num text-[12px] text-g500">
        пересчитано {{ s.updatedAt.value }}
      </span>
    </header>

    <p v-if="s.error.value" role="alert" class="rounded-lg border border-g300 px-3 py-2 text-[13px]">
      {{ s.error.value }}
    </p>

    <div v-if="s.isLoading.value" class="flex flex-col gap-2">
      <div v-for="i in 8" :key="i" class="h-9 animate-pulse rounded-lg bg-g75" />
    </div>

    <template v-else>
      <div class="flex flex-wrap gap-x-10 gap-y-3">
        <div class="flex flex-col">
          <span class="jb-label">Всего раздач</span>
          <span class="jb-num text-2xl font-semibold">
            {{ formatStatNumberFull(s.total.value.alltorrents) }}
          </span>
        </div>
        <div class="flex flex-col">
          <span class="jb-label">Источников</span>
          <span class="jb-num text-2xl font-semibold">{{ s.total.value.count }}</span>
        </div>
        <div class="flex flex-col">
          <span class="jb-label">Новых за сутки</span>
          <span class="jb-num text-2xl font-semibold">
            {{ formatStatNumberFull(s.total.value.newtor) }}
          </span>
        </div>
        <div class="flex flex-col">
          <span class="jb-label">Дорожки разобраны</span>
          <span class="jb-num text-2xl font-semibold">
            {{ formatStatNumberFull(s.total.value.confirm) }}
          </span>
        </div>
        <div v-if="s.quality.value?.imdbCodes" class="flex flex-col">
          <span class="jb-label">Кодов IMDB</span>
          <span class="jb-num text-2xl font-semibold">
            {{ formatStatNumberFull(s.quality.value.imdbCodes) }}
          </span>
        </div>
      </div>

      <!-- Чему верить в выдаче. Объёмы базы отвечают на вопрос «сколько
           накоплено», а человека занимает другое: свежие ли числа он видит
           и почему у части раздач счётчик полый. -->
      <section v-if="s.quality.value" class="flex flex-col gap-2 rounded-xl bg-g75 px-4 py-3">
        <h2 class="jb-label">Откуда берутся числа раздающих</h2>
        <div class="flex flex-wrap gap-x-8 gap-y-2 text-[12.5px] text-g700">
          <p>
            <span class="text-g500">Спрашиваем в момент поиска:</span>
            {{ s.quality.value.liveSeeders?.inline?.join(', ') }}
          </p>
          <p>
            <span class="text-g500">Спрашиваем после ответа, помним четверть часа:</span>
            {{ s.quality.value.liveSeeders?.background?.join(', ') }}
          </p>
          <p>
            <span class="text-g500">Опрашиваем анонс:</span>
            {{ s.quality.value.liveSeeders?.scrape?.join(', ') }}
          </p>
          <p>
            <span class="text-g500">Счётчиков не публикует:</span>
            {{ s.quality.value.liveSeeders?.silent?.join(', ') }}
          </p>
        </div>
        <p class="text-[12.5px] text-g500">
          Запись, обновлённая обходом за последние
          {{ s.quality.value.freshHours }} ч, тоже считается проверенной — обход
          читает листинг трекера, а там колонка сидов живая.
          <template v-if="s.quality.value.deadReleases">
            Раздач, опознанных удалёнными с трекера и убранных из выдачи:
            <b class="text-ink">{{ s.quality.value.deadReleases }}</b>.
          </template>
        </p>
      </section>

      <!-- Что делается прямо сейчас. Глубокие обходы идут по восемь-одиннадцать
           часов, и до сих пор увидеть это можно было только по ssh. -->
      <section
        v-if="s.crawlRunning.value.length || s.crawlQueues.value.length"
        class="flex flex-col gap-2 rounded-xl bg-g75 px-4 py-3"
      >
        <h2 class="jb-label">Обходы</h2>

        <p v-if="s.crawlRunning.value.length" class="text-[12.5px] text-g700">
          <span class="text-g500">Идёт сейчас:</span>
          <template v-for="(r, i) in s.crawlRunning.value" :key="r.tracker">
            <template v-if="i">, </template>
            <b class="text-ink">{{ getTrackerDisplayName(r.tracker) }}</b>
            — {{ formatDuration(r.minutes) }}
          </template>
        </p>
        <p v-else class="text-[12.5px] text-g500">Сейчас ни один обход не идёт.</p>

        <p v-if="s.crawlQueues.value.length" class="jb-num text-[12.5px] text-g700">
          <span class="jb-label">Осталось разобрать</span>
          <template v-for="q in s.crawlQueues.value" :key="q.tracker + q.kind">
            <span class="ml-3 inline-block">
              {{ getTrackerDisplayName(q.tracker) }}
              <span class="text-g500">{{ q.kind }}</span>
              {{ formatStatNumberFull(q.value) }}
            </span>
          </template>
        </p>

        <p v-if="s.crawlFinished.value.length" class="text-[12.5px] text-g500">
          Последние заходы:
          <template v-for="(r, i) in s.crawlFinished.value" :key="r.tracker">
            <template v-if="i">, </template>
            {{ getTrackerDisplayName(r.tracker) }} — {{ r.outcome }},
            {{ formatDuration(r.minutes) }}
          </template>
        </p>
      </section>

      <!-- Источник, который отвечает, но ничего не выкладывает, по нашим
           числам неотличим от живого: код 200, непустой ответ, растущий
           счётчик обновлений. Поэтому называем таких вслух. -->
      <section
        v-if="s.silent.value.length"
        class="flex flex-col gap-1 rounded-xl border border-g300 px-4 py-3"
      >
        <h2 class="jb-label">Источники, которые давно ничего не выкладывали</h2>
        <p class="text-[12.5px] text-g700">
          <template v-for="(f, i) in s.silent.value" :key="f.tracker">
            <template v-if="i">, </template>
            <b class="text-ink">{{ getTrackerDisplayName(f.tracker) }}</b>
            — {{ formatSilence(f.silentDays).text }}
          </template>
        </p>
        <p class="text-[12.5px] text-g500">
          Считается по самой свежей дате публикации у самого источника, а не по
          нашим записям: трекер может исправно отвечать и отдавать при этом
          прошлогоднее.
        </p>
      </section>

      <div class="overflow-x-auto">
        <table class="w-full border-collapse">
          <thead>
            <tr class="border-b border-g300">
              <th class="jb-label pr-4 pb-2 text-left font-normal">Источник</th>
              <th class="jb-label pr-4 pb-2 text-right font-normal">Раздач</th>
              <th class="jb-label pr-4 pb-2 text-right font-normal">Новых</th>
              <th class="jb-label pr-4 pb-2 text-right font-normal">Обновлено</th>
              <th class="jb-label pr-4 pb-2 text-right font-normal">Проверено</th>
              <th class="jb-label pr-4 pb-2 text-right font-normal">Дорожки</th>
              <th class="jb-label pr-4 pb-2 text-right font-normal">Последняя</th>
              <th class="jb-label pb-2 text-right font-normal">У источника</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="t in s.sorted.value" :key="t.trackerName || ''" class="border-b border-g150">
              <td class="py-2.5 pr-4 text-[13.5px]">{{ getTrackerDisplayName(t.trackerName) }}</td>
              <td class="jb-num py-2.5 pr-4 text-right text-[13px]">
                {{ formatStatNumberFull(t.alltorrents) }}
              </td>
              <td
                class="jb-num py-2.5 pr-4 text-right text-[13px]"
                :class="!t.newtor ? 'text-g500' : ''"
              >
                {{ formatStatNumberFull(t.newtor) }}
              </td>
              <td class="jb-num py-2.5 pr-4 text-right text-[13px] text-g700">
                {{ formatStatNumberFull(t.update) }}
              </td>
              <td class="jb-num py-2.5 pr-4 text-right text-[13px] text-g700">
                {{ formatStatNumberFull(t.check) }}
              </td>
              <td class="jb-num py-2.5 pr-4 text-right text-[13px] text-g700">
                {{ formatStatNumberFull(getTracksData(t).confirm) }}
              </td>
              <td class="jb-num py-2.5 pr-4 text-right text-[12.5px] text-g500">
                {{ t.lastnewtor || '—' }}
              </td>
              <td
                class="jb-num py-2.5 text-right text-[12.5px]"
                :class="formatSilence(s.freshnessOf(t.trackerName)?.silentDays).alarming
                  ? 'font-medium text-ink'
                  : 'text-g500'"
              >
                {{ formatSilence(s.freshnessOf(t.trackerName)?.silentDays).text }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <p class="max-w-prose text-[12.5px] text-g500">
        «Дорожки» — сколько раздач с разобранным содержимым файла. Разбор требует
        живых пиров, поэтому у мёртвых раздач его не бывает. «Последняя» — когда
        мы записали новую раздачу, «У источника» — сколько времени прошло с самой
        свежей публикации на самом трекере. Расходятся эти две колонки как раз
        тогда, когда что-то не так.
      </p>
    </template>
  </section>
</template>
