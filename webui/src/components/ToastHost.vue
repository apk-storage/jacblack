<script setup lang="ts">
import Icon from '@/components/Icon.vue'
import { useToast } from '@/composables/useToast'

/**
 * Стопка сообщений в правом нижнем углу.
 *
 * Внизу, а не сверху: сверху шапка с поиском, и сообщение перекрывало бы
 * то место, куда человек только что нажал. По клику закрывается сразу —
 * ждать четыре секунды никого не заставляем.
 */
const { items, dismiss } = useToast()
</script>

<template>
  <div
    class="pointer-events-none fixed inset-x-3 bottom-3 z-50 flex flex-col items-end gap-2 sm:inset-x-auto sm:right-4 sm:bottom-4"
    role="status"
    aria-live="polite"
  >
    <button
      v-for="t in items"
      :key="t.id"
      type="button"
      class="pointer-events-auto flex w-full max-w-[380px] items-start gap-2 rounded-xl border border-g150 bg-paper px-3 py-2.5 text-left text-[13px] leading-snug text-ink shadow-[var(--jb-shadow)]"
      @click="dismiss(t.id)"
    >
      <Icon
        :name="t.kind === 'error' ? 'alert' : t.kind === 'success' ? 'check' : 'info'"
        :size="15"
        class="mt-px shrink-0"
        :class="{
          'text-[var(--jb-live)]': t.kind === 'success',
          'text-[#c2410c]': t.kind === 'error',
          'text-g500': t.kind === 'info',
        }"
      />
      <span>{{ t.text }}</span>
    </button>
  </div>
</template>
