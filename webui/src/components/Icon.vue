<script setup lang="ts">
import { computed } from 'vue'

/**
 * Значки рисуем своим набором, а не библиотекой.
 *
 * В прежнем приложении `@lucide/vue` тянул за собой кусок на 144 КБ ради
 * полутора десятков картинок. Здесь их ровно столько, сколько нужно, и
 * весят они байты. Набор в контурном стиле 24×24, толщина линии одна —
 * значки должны выглядеть роднёй, а не сборной солянкой.
 */
const PATHS: Record<string, string> = {
  search: 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14M16 16l4.5 4.5',
  filter: 'M4 6h16M7 12h10M10 18h4',
  close: 'M6 6l12 12M18 6L6 18',
  chevronLeft: 'M14.5 5.5 8 12l6.5 6.5',
  chevronRight: 'M9.5 5.5 16 12l-6.5 6.5',
  chevronDown: 'M5.5 9.5 12 16l6.5-6.5',
  check: 'M4.5 12.5 9.5 17.5 19.5 6.5',
  sun: 'M12 4.5v-2M12 21.5v-2M4.5 12h-2M21.5 12h-2M6.7 6.7 5.3 5.3M18.7 18.7l-1.4-1.4M6.7 17.3l-1.4 1.4M18.7 5.3l-1.4 1.4M12 7.5a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9',
  moon: 'M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5',
  chart: 'M4 20V10M10 20V4M16 20v-7M22 20H2',
  external: 'M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5',
  magnet: 'M6 4H3v7a9 9 0 0 0 18 0V4h-3v7a6 6 0 0 1-12 0zM3 9h3M18 9h3',
  server: 'M4 5h16v5H4zM4 14h16v5H4zM7.5 7.5h.01M7.5 16.5h.01',
  play: 'M8 5.5v13l11-6.5z',
  sort: 'M7 4v16M7 20l-3-3M7 20l3-3M17 20V4M17 4l-3 3M17 4l3 3',
  info: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18M12 11v6M12 7.5h.01',
  alert: 'M12 3 2.5 20h19zM12 10v4.5M12 17.5h.01',
}

const props = withDefaults(
  defineProps<{ name: keyof typeof PATHS | string; size?: number }>(),
  { size: 16 },
)

const d = computed(() => PATHS[props.name] ?? '')
</script>

<template>
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
    focusable="false"
  >
    <path :d="d" />
  </svg>
</template>
