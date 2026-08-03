import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { h } from 'vue'

/**
 * Маршрутизатор здесь отвечает только за адрес: какой экран открыт и что
 * лежит в параметрах запроса (поисковая фраза, фильтры, сортировка). Сами
 * экраны выбирает оболочка — потому что на большом экране и на телефоне это
 * разные компоненты, а решение о раскладке принимается ровно в одном месте
 * (см. useLayout).
 *
 * Отсюда пустышка вместо component: если бы маршрут указывал на конкретный
 * компонент, выбор раскладки размазался бы по маршрутам.
 */
const Passthrough = { render: () => h('div') }

const routes: RouteRecordRaw[] = [
  { path: '/', name: 'search', component: Passthrough },
  { path: '/stats', name: 'stats', component: Passthrough },
  { path: '/:pathMatch(.*)*', redirect: '/' },
]

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
  scrollBehavior(to, from, saved) {
    if (saved) return saved
    // Смена фильтров и сортировки меняет адрес, но это не переход:
    // прокрутку в таком случае трогать нельзя.
    if (to.name === from.name) return false
    return { top: 0 }
  },
})

export default router
