<script setup lang="ts">
import { computed } from 'vue'

/**
 * Число сидов с отметкой состояния.
 *
 * Четыре вида, а не два.
 *
 * Проверенное — трекер ответил прямо сейчас либо запись обновлена обходом
 * за последние три часа. Непроверенное — снимок из базы, а он бывает
 * годовалым: 98.8% записей старше года. Показывать такое наравне с живым
 * значит выдавать старое за свежее, поэтому у него полый контур.
 *
 * Отдельно — «трекер не сообщает». У lostfilm счётчиков нет вовсе, и
 * единица в записи проставлена разбором, а не данными. Показывать её
 * числом — врать; ставим прочерк.
 *
 * И ноль: трекер ответил пустым. Это не доказательство смерти — раздача
 * может жить на DHT, — но выбирать её последней разумно.
 */
const props = withDefaults(
  defineProps<{
    value: number | null | undefined
    verified?: boolean
    /** Трекер не публикует счётчиков — число показывать нельзя. */
    unknown?: boolean
    size?: 'sm' | 'lg'
  }>(),
  { verified: undefined, unknown: false, size: 'sm' },
)

const count = computed(() => Number(props.value) || 0)

const state = computed(() => {
  if (props.unknown) return 'jb-seed--unknown'
  if (count.value <= 0) return 'jb-seed--zero'
  return props.verified === false ? 'jb-seed--stale' : ''
})

const title = computed(() => {
  if (props.unknown) return 'Трекер не сообщает число раздающих'
  if (count.value <= 0) return 'Сидов нет: трекер ответил пустым. Раздача может жить на DHT.'
  if (props.verified === false) return 'Число из базы: может быть годовалой давности'
  return 'Проверено сейчас'
})
</script>

<template>
  <span
    class="jb-seed"
    :class="[state, size === 'lg' ? 'text-[19px]' : 'text-[13px]']"
    :title="title"
  >
    {{ unknown ? '—' : count }}
  </span>
</template>
