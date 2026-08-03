<script setup lang="ts">
import { ref, watch } from 'vue'
import Icon from '@/components/Icon.vue'
import { getItem, setItem, StorageKeys } from '@/lib/storage'

/**
 * Куда отправлять раздачу.
 *
 * Отдельного экрана настроек в интерфейсе нет, а адрес TorrServer знать
 * обязательно — иначе кнопка «В TorrServer» не может сделать то, что
 * обещает надписью. Поэтому спрашиваем ровно в тот момент, когда адрес
 * впервые понадобился, и запоминаем в браузере.
 *
 * Логин и пароль необязательны: у TorrServer вход включают не всегда.
 */
const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: []; saved: [] }>()

const url = ref('')
const login = ref('')
const password = ref('')

watch(
  () => props.open,
  (open) => {
    if (!open) return
    url.value = getItem(StorageKeys.torrServerUrl) ?? ''
    login.value = getItem(StorageKeys.torrServerLogin) ?? ''
    password.value = getItem(StorageKeys.torrServerPassword) ?? ''
  },
  { immediate: true },
)

function save() {
  const value = url.value.trim()
  if (!value) return

  setItem(StorageKeys.torrServerUrl, value)
  setItem(StorageKeys.torrServerLogin, login.value.trim())
  setItem(StorageKeys.torrServerPassword, password.value)

  emit('saved')
}
</script>

<template>
  <div
    v-if="open"
    class="fixed inset-0 z-40 flex items-center justify-center bg-black/40 px-4"
    @click.self="emit('close')"
  >
    <div class="w-full max-w-[420px] rounded-2xl bg-paper p-4 shadow-[var(--jb-shadow)]">
      <div class="mb-3 flex items-center justify-between">
        <h2 class="text-[15px] font-medium text-ink">Адрес TorrServer</h2>
        <button
          type="button"
          class="rounded-lg p-1 text-g500 hover:text-ink"
          aria-label="Закрыть"
          @click="emit('close')"
        >
          <Icon name="close" :size="16" />
        </button>
      </div>

      <p class="mb-3 text-[12.5px] leading-snug text-g500">
        Раздача уйдёт прямо в TorrServer, а не в торрент-качалку. Адрес хранится
        только в этом браузере.
      </p>

      <form class="flex flex-col gap-2" @submit.prevent="save">
        <input
          v-model="url"
          type="url"
          required
          placeholder="http://192.168.1.10:8090"
          aria-label="Адрес TorrServer"
          class="h-9 rounded-lg border border-g150 bg-page px-2.5 text-[13px] text-ink outline-none focus:border-g300"
        />
        <div class="flex gap-2">
          <input
            v-model="login"
            type="text"
            placeholder="Логин, если нужен"
            aria-label="Логин TorrServer"
            class="h-9 min-w-0 flex-1 rounded-lg border border-g150 bg-page px-2.5 text-[13px] text-ink outline-none focus:border-g300"
          />
          <input
            v-model="password"
            type="password"
            placeholder="Пароль"
            aria-label="Пароль TorrServer"
            class="h-9 min-w-0 flex-1 rounded-lg border border-g150 bg-page px-2.5 text-[13px] text-ink outline-none focus:border-g300"
          />
        </div>

        <button
          type="submit"
          class="mt-1 h-9 rounded-lg bg-ink text-[13px] text-paper disabled:opacity-50"
          :disabled="!url.trim()"
        >
          Сохранить и отправить
        </button>
      </form>
    </div>
  </div>
</template>
