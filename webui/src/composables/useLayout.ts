import { computed, ref, onMounted, onUnmounted } from 'vue'

/**
 * Единственная точка, где решается, какая раскладка показывается.
 *
 * Зачем так строго. В прежнем приложении проверок «если мобильный» была
 * россыпь по всему дереву, и кончилось это тем, что девять полей фильтра
 * оказались написаны дважды — отдельно для шторки и отдельно для панели,
 * около 380 строк, которые надо править синхронно. Правило простое:
 * ветвление здесь и больше нигде; оболочки получают уже готовый ответ.
 *
 * Опора — ширина, при которой раскладка перестаёт работать, а не список
 * устройств: планшетов, складных телефонов и «странных мониторов» не
 * перечислить, и любой такой список устаревает.
 *
 * 900 — граница, ниже которой панель фильтров съедает треть ширины.
 * Планшет в книжной ориентации (768) попадает в мобильную раскладку
 * намеренно, в альбомной (1024) — в десктопную.
 */
export const LAYOUT_BREAKPOINT = 900

/** Ниже этой ширины панель фильтров по умолчанию свёрнута в полосу. */
export const PANEL_AUTO_COLLAPSE = 1200

export type LayoutKind = 'desktop' | 'mobile'

const width = ref(typeof window === 'undefined' ? LAYOUT_BREAKPOINT : window.innerWidth)

let listeners = 0
let detach: (() => void) | null = null

function attach() {
  if (typeof window === 'undefined' || detach) return

  let frame = 0
  const onResize = () => {
    if (frame) return
    frame = requestAnimationFrame(() => {
      frame = 0
      width.value = window.innerWidth
    })
  }

  window.addEventListener('resize', onResize, { passive: true })
  window.addEventListener('orientationchange', onResize, { passive: true })

  detach = () => {
    if (frame) cancelAnimationFrame(frame)
    window.removeEventListener('resize', onResize)
    window.removeEventListener('orientationchange', onResize)
    detach = null
  }
}

export function useLayout() {
  onMounted(() => {
    listeners += 1
    width.value = window.innerWidth
    attach()
  })

  onUnmounted(() => {
    listeners -= 1
    if (listeners <= 0) {
      listeners = 0
      detach?.()
    }
  })

  const kind = computed<LayoutKind>(() =>
    width.value >= LAYOUT_BREAKPOINT ? 'desktop' : 'mobile',
  )

  return {
    width,
    kind,
    isDesktop: computed(() => kind.value === 'desktop'),
    /** Узкий большой экран: панель показываем свёрнутой, пока её не раскроют. */
    prefersCollapsedPanel: computed(() => width.value < PANEL_AUTO_COLLAPSE),
  }
}
