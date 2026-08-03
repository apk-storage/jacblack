import { ref } from 'vue'

/**
 * Короткие сообщения поверх страницы.
 *
 * Зачем понадобились. Действия карточки ничего не сообщали о себе: отправка
 * в TorrServer молча уходила в никуда, а кнопка «В Лампе» просто открывала
 * magnet — то есть делала не то, что обещала надписью. Без ответа интерфейса
 * человек не понимает, сработало или нет, и жмёт второй раз.
 *
 * Держим один общий список на всё приложение: сообщений мало, а два
 * независимых хранилища для большого экрана и телефона разошлись бы.
 */
export type ToastKind = 'info' | 'success' | 'error'

export type Toast = {
  id: number
  kind: ToastKind
  text: string
}

const items = ref<Toast[]>([])
let nextId = 1

/** Сколько держать сообщение на экране. Ошибку дольше — её читают. */
const LIFETIME: Record<ToastKind, number> = {
  info: 3200,
  success: 2600,
  error: 5000,
}

export function useToast() {
  function show(text: string, kind: ToastKind = 'info') {
    const id = nextId++
    items.value = [...items.value, { id, kind, text }]

    setTimeout(() => dismiss(id), LIFETIME[kind])
  }

  function dismiss(id: number) {
    items.value = items.value.filter((t) => t.id !== id)
  }

  return {
    items,
    show,
    info: (text: string) => show(text, 'info'),
    success: (text: string) => show(text, 'success'),
    error: (text: string) => show(text, 'error'),
    dismiss,
  }
}
