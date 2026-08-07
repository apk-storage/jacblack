<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import Icon from '@/components/Icon.vue'
import { useAdmin } from '@/composables/useAdmin'

/**
 * Оболочка большого экрана: шапка и экран под ней.
 *
 * Экран выбирается по имени маршрута прямо здесь, а не маршрутизатором:
 * на телефоне это другие компоненты, и решение о раскладке принято выше,
 * в App.vue. Так выбор раскладки остаётся в одном месте.
 */
const SearchDesktop = defineAsyncComponent(() => import('@/screens/SearchDesktop.vue'))
const StatsDesktop = defineAsyncComponent(() => import('@/screens/StatsDesktop.vue'))

const route = useRoute()
const { isAdmin } = useAdmin()

// Статистика — админская операционка, публике не показываем. Без ключа
// маршрут /stats просто отдаёт поиск, а вкладки в шапке нет.
const screen = computed(() =>
  route.name === 'stats' && isAdmin.value ? StatsDesktop : SearchDesktop,
)

const nav = computed(() => [
  { to: '/', name: 'search', label: 'Поиск', icon: 'search' },
  ...(isAdmin.value ? [{ to: '/stats', name: 'stats', label: 'Статистика', icon: 'chart' }] : []),
])
</script>

<template>
  <div class="flex min-h-dvh flex-col bg-page">
    <a
      href="#screen"
      class="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-md focus:bg-ink focus:px-3 focus:py-2 focus:text-paper"
    >
      К содержимому
    </a>

    <header class="sticky top-0 z-40 border-b border-g150 bg-paper">
      <div class="mx-auto flex h-13 max-w-[1280px] items-center gap-4 px-6" style="height: var(--jb-header)">
        <RouterLink to="/" class="flex shrink-0 items-center gap-2 text-ink no-underline">
          <span class="block size-4 rounded-[3px] bg-ink"></span>
          <span class="text-[15px] font-bold tracking-tight">JacBlack</span>
        </RouterLink>

        <nav class="flex items-center gap-1">
          <RouterLink
            v-for="item in nav"
            :key="item.to"
            :to="item.to"
            :aria-current="route.name === item.name ? 'page' : undefined"
            class="flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-[13px] no-underline"
            :class="route.name === item.name ? 'bg-g75 font-medium text-ink' : 'text-g500 hover:text-ink'"
          >
            <Icon :name="item.icon" :size="15" />
            {{ item.label }}
          </RouterLink>
        </nav>

      </div>
    </header>

    <main id="screen" tabindex="-1" class="mx-auto w-full max-w-[1280px] flex-1 outline-none">
      <component :is="screen" />
    </main>
  </div>
</template>
