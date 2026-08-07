<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import Icon from '@/components/Icon.vue'
import { useAdmin } from '@/composables/useAdmin'

/**
 * Оболочка телефона: узкая шапка сверху, переходы снизу.
 *
 * Переходы внизу, а не под шапкой, — до них дотягивается большой палец.
 * Отступы учитывают безопасную зону, иначе на iPhone нижний ряд уезжает
 * под системную полосу.
 */
const SearchMobile = defineAsyncComponent(() => import('@/screens/SearchMobile.vue'))
const StatsMobile = defineAsyncComponent(() => import('@/screens/StatsMobile.vue'))

const route = useRoute()
const { isAdmin } = useAdmin()

// Статистика — админская, публике не показываем (см. useAdmin).
const screen = computed(() =>
  route.name === 'stats' && isAdmin.value ? StatsMobile : SearchMobile,
)

const nav = computed(() => [
  { to: '/', name: 'search', label: 'Поиск', icon: 'search' },
  ...(isAdmin.value ? [{ to: '/stats', name: 'stats', label: 'Статистика', icon: 'chart' }] : []),
])
</script>

<template>
  <div class="flex min-h-dvh flex-col bg-page">
    <header
      class="sticky top-0 z-40 border-b border-g150 bg-paper"
      style="padding-top: env(safe-area-inset-top)"
    >
      <div class="flex h-12 items-center gap-2 px-4">
        <span class="block size-4 rounded-[3px] bg-ink"></span>
        <span class="text-[15px] font-bold tracking-tight">JacBlack</span>
      </div>
    </header>

    <main class="flex-1">
      <component :is="screen" />
    </main>

    <nav
      class="sticky bottom-0 z-40 flex border-t border-g150 bg-paper"
      style="padding-bottom: env(safe-area-inset-bottom)"
      aria-label="Разделы"
    >
      <RouterLink
        v-for="item in nav"
        :key="item.to"
        :to="item.to"
        :aria-current="route.name === item.name ? 'page' : undefined"
        class="flex flex-1 flex-col items-center gap-1 py-2 text-[10.5px] no-underline"
        :class="route.name === item.name ? 'text-ink' : 'text-g500'"
      >
        <Icon :name="item.icon" :size="17" />
        {{ item.label }}
      </RouterLink>
    </nav>
  </div>
</template>
