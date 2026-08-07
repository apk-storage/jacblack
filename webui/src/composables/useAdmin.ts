import { computed } from 'vue'
import { useRoute } from 'vue-router'

/**
 * Гейт для админской статистики.
 *
 * Это ОБСКУРНОСТЬ, а не безопасность: числа обходов не секретны, задача —
 * не показывать внутреннюю операционку обычным посетителям публичного jac.black.
 * Ключ живёт в клиентском бандле, поэтому «защитой» не является; кто знает адрес
 * с ключом — увидит. Для несекретных данных этого достаточно.
 *
 * Открыть: /stats?key=<KEY> — флаг запомнится в localStorage, дальше ключ не нужен.
 * Сбросить: /?admin=off
 */
const KEY = 'kq7m2'

export function useAdmin() {
  const route = useRoute()

  if (route.query.admin === 'off') {
    try { localStorage.removeItem('jb_admin') } catch { /* ignore */ }
  } else if (route.query.key === KEY) {
    try { localStorage.setItem('jb_admin', '1') } catch { /* ignore */ }
  }

  const isAdmin = computed(() => {
    if (route.query.key === KEY) return true
    try { return localStorage.getItem('jb_admin') === '1' } catch { return false }
  })

  return { isAdmin }
}
