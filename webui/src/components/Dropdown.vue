<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import Icon from '@/components/Icon.vue'

/**
 * Раскрывающийся список в теме сайта.
 *
 * Системный `<select>` не годится: закрытое состояние ещё поддаётся
 * оформлению, а раскрытый список рисует операционная система, и он
 * выпадает из вида страницы. Здесь список свой, поэтому выглядит так же,
 * как всё остальное.
 *
 * Клавиатура при этом не потеряна: Esc закрывает, стрелки переводят между
 * пунктами, Enter выбирает — иначе своя реализация была бы шагом назад
 * по сравнению с системной.
 */
/**
 * Свойство названо `name`, а не `ariaLabel`: `aria-label` Vue считает
 * обычным атрибутом разметки и в свойство не переводит — проверка типов
 * на этом спотыкается.
 */
const props = defineProps<{
  modelValue: string
  options: readonly { value: string; label: string }[]
  name: string
}>()

const emit = defineEmits<{ 'update:modelValue': [string] }>()

const open = ref(false)
const root = ref<HTMLElement | null>(null)
const active = ref(0)

function current() {
  return props.options.find((o) => o.value === props.modelValue)
}

function toggle() {
  open.value = !open.value
  if (open.value) {
    active.value = Math.max(0, props.options.findIndex((o) => o.value === props.modelValue))
  }
}

function pick(value: string) {
  emit('update:modelValue', value)
  open.value = false
}

function onKeydown(e: KeyboardEvent) {
  if (!open.value) {
    if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown') {
      e.preventDefault()
      toggle()
    }
    return
  }

  if (e.key === 'Escape') {
    e.preventDefault()
    open.value = false
    return
  }
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    active.value = (active.value + 1) % props.options.length
    return
  }
  if (e.key === 'ArrowUp') {
    e.preventDefault()
    active.value = (active.value - 1 + props.options.length) % props.options.length
    return
  }
  if (e.key === 'Enter') {
    e.preventDefault()
    const opt = props.options[active.value]
    if (opt) pick(opt.value)
  }
}

function onOutside(e: MouseEvent) {
  if (open.value && root.value && !root.value.contains(e.target as Node)) open.value = false
}

onMounted(() => document.addEventListener('mousedown', onOutside))
onBeforeUnmount(() => document.removeEventListener('mousedown', onOutside))
</script>

<template>
  <div ref="root" class="relative shrink-0">
    <button
      type="button"
      class="flex h-10 items-center gap-2 rounded-[9px] bg-g75 px-3 text-[13px] text-ink"
      :aria-label="name"
      :aria-expanded="open"
      aria-haspopup="listbox"
      @click="toggle"
      @keydown="onKeydown"
    >
      <span class="whitespace-nowrap">{{ current()?.label ?? '' }}</span>
      <Icon name="chevronDown" :size="14" class="text-g500" />
    </button>

    <ul
      v-if="open"
      class="absolute right-0 z-50 mt-1 min-w-full overflow-hidden rounded-[10px] border border-g150 bg-raised py-1 shadow-[var(--jb-shadow)]"
      role="listbox"
      :aria-label="name"
    >
      <li v-for="(o, i) in options" :key="o.value" role="none">
        <button
          type="button"
          role="option"
          :aria-selected="o.value === modelValue"
          class="flex w-full items-center gap-2 px-3 py-2 text-left text-[13px] whitespace-nowrap"
          :class="[
            o.value === modelValue ? 'text-ink' : 'text-g700',
            i === active ? 'bg-g75' : '',
          ]"
          @click="pick(o.value)"
          @mouseenter="active = i"
        >
          <Icon
            name="check"
            :size="14"
            :class="o.value === modelValue ? 'text-ink' : 'opacity-0'"
          />
          {{ o.label }}
        </button>
      </li>
    </ul>
  </div>
</template>
