<script setup lang="ts">
import { defineAsyncComponent, computed } from 'vue'
import ToastHost from '@/components/ToastHost.vue'
import { useLayout } from '@/composables/useLayout'

/**
 * Корень приложения: выбирает оболочку и больше ничего не делает.
 *
 * Обе оболочки подгружаются отдельными кусками, поэтому телефон не скачивает
 * десктопный код, а большой экран — мобильный. Состояние поиска живёт выше
 * оболочек (см. useSearch) и переживает переключение: при повороте планшета
 * выдача не перезапрашивается.
 */
const DesktopShell = defineAsyncComponent(
  () => import('@/shells/DesktopShell.vue'),
)
const MobileShell = defineAsyncComponent(
  () => import('@/shells/MobileShell.vue'),
)

const { kind } = useLayout()
const shell = computed(() => (kind.value === 'desktop' ? DesktopShell : MobileShell))
</script>

<template>
  <component :is="shell" />
  <!-- Сообщения общие для обеих оболочек: держим их здесь, чтобы не
       заводить два независимых хранилища, которые неминуемо разойдутся. -->
  <ToastHost />
</template>
