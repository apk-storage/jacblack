import { createApp } from 'vue'
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import App from './App.vue'
import router from './router'
import './styles/tokens.css'

/**
 * Выдача живёт в кеше пять минут и не перезапрашивается при возврате на
 * вкладку: сиды опрашиваются на сервере с бюджетом 800 мс, и дёргать поиск
 * заново каждый раз, когда человек переключился в другое окно, незачем.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 60_000,
      gcTime: 5 * 60_000,
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
})

createApp(App)
  .use(router)
  .use(VueQueryPlugin, { queryClient })
  .mount('#app')
