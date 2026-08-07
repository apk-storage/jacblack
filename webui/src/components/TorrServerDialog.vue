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
const test = ref<{ state: 'idle' | 'checking' | 'ok' | 'fail'; text: string }>({
  state: 'idle',
  text: '',
})

watch(
  () => props.open,
  (open) => {
    if (!open) return
    url.value = getItem(StorageKeys.torrServerUrl) ?? ''
    login.value = getItem(StorageKeys.torrServerLogin) ?? ''
    password.value = getItem(StorageKeys.torrServerPassword) ?? ''
    test.value = { state: 'idle', text: '' }
  },
  { immediate: true },
)

// Проверка связи через бэкенд jac.black (сервер→TorrServer), а не из браузера:
// прямой запрос в HTTP-TorrServer браузер режет (mixed content), и «проверить»
// из веба всегда падало бы не по делу.
async function check() {
  const value = url.value.trim()
  if (!value) return
  test.value = { state: 'checking', text: 'Проверяю…' }
  try {
    const res = await fetch(`/torrserver/check?baseUrl=${encodeURIComponent(value)}`)
    const data = (await res.json().catch(() => null)) as
      | { ok?: boolean; version?: string; status?: number }
      | null
    if (data?.ok) {
      test.value = { state: 'ok', text: `На связи${data.version ? ` — ${data.version}` : ''}` }
    } else if (data?.status === 401) {
      test.value = { state: 'ok', text: 'Отвечает, но требует логин/пароль' }
    } else {
      test.value = { state: 'fail', text: 'Не отвечает — проверьте адрес и что сервер запущен' }
    }
  } catch {
    test.value = { state: 'fail', text: 'Не удалось проверить' }
  }
}

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

      <form class="flex flex-col gap-2" autocomplete="off" @submit.prevent="save">
        <input
          v-model="url"
          type="url"
          required
          autocomplete="off"
          placeholder="http://91.186.219.250:8043"
          aria-label="Адрес TorrServer"
          class="h-9 rounded-lg border border-g150 bg-page px-2.5 text-[13px] text-ink outline-none focus:border-g300"
        />
        <div class="flex gap-2">
          <input
            v-model="login"
            type="text"
            autocomplete="off"
            placeholder="Логин, если нужен"
            aria-label="Логин TorrServer"
            class="h-9 min-w-0 flex-1 rounded-lg border border-g150 bg-page px-2.5 text-[13px] text-ink outline-none focus:border-g300"
          />
          <input
            v-model="password"
            type="password"
            autocomplete="new-password"
            placeholder="Пароль"
            aria-label="Пароль TorrServer"
            class="h-9 min-w-0 flex-1 rounded-lg border border-g150 bg-page px-2.5 text-[13px] text-ink outline-none focus:border-g300"
          />
        </div>

        <p
          v-if="test.text"
          class="text-[12px]"
          :class="test.state === 'ok' ? 'text-g700' : test.state === 'fail' ? 'text-red-500' : 'text-g500'"
        >
          {{ test.text }}
        </p>

        <div class="mt-1 flex gap-2">
          <button
            type="button"
            class="h-9 flex-1 rounded-lg border border-g150 text-[13px] text-ink disabled:opacity-50"
            :disabled="!url.trim() || test.state === 'checking'"
            @click="check"
          >
            Проверить
          </button>
          <button
            type="submit"
            class="h-9 flex-1 rounded-lg bg-ink text-[13px] text-paper disabled:opacity-50"
            :disabled="!url.trim()"
          >
            Сохранить
          </button>
        </div>
      </form>
    </div>
  </div>
</template>
