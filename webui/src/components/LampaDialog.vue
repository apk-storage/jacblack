<script setup lang="ts">
import { computed, ref } from 'vue'
import Icon from '@/components/Icon.vue'
import { useCub } from '@/composables/useCub'
import type { TorrentItem } from '@/lib/torrents'
import type { LampaLaunch } from '@/lib/cub/eval-payload'
import type { CubDevice } from '@/lib/cub/socket'

/**
 * «В Лампе» — запустить раздачу на устройстве Лампы (ТВ) через CUB.
 *
 * Экрана настроек нет, поэтому всё нужное спрашиваем прямо здесь и по шагам:
 *   1. если не входили в cub.rip — код добавления устройства (берётся на
 *      cub.rip/add в своей учётке);
 *   2. код терминала — тот, что задан в Лампе на ТВ (без него устройство
 *      откажется выполнять запуск);
 *   3. выбор устройства из списка — по нему и запускаем.
 *
 * Почему так, а не «просто magnet»: запуск ИМЕННО этой раздачи на удалённом ТВ
 * возможен только через терминал Лампы (см. lampac/JACBLACK-V-LAMPE.md).
 */
const props = defineProps<{ open: boolean; item: TorrentItem | null }>()
const emit = defineEmits<{ close: [] }>()

const cub = useCub()

const code = ref('')
const busy = ref(false)
const error = ref('')
const done = ref('')

const launch = computed<LampaLaunch | null>(() => {
  const it = props.item
  if (!it || !it.magnet) return null
  const year = typeof it.relased === 'number' ? it.relased : parseInt(String(it.relased ?? ''), 10)
  return {
    magnet: it.magnet,
    title: it.title || it.name || 'Раздача',
    imdb: it.imdb || null,
    year: Number.isFinite(year) ? year : null,
  }
})

async function signIn() {
  error.value = ''
  busy.value = true
  try {
    await cub.login(code.value)
    cub.refreshDevices()
  } catch (e) {
    error.value = (e as Error).message || 'Не удалось войти'
  } finally {
    busy.value = false
  }
}

function run(device: CubDevice) {
  error.value = ''
  done.value = ''
  const data = launch.value
  if (!data) { error.value = 'У раздачи нет magnet-ссылки'; return }
  try {
    cub.launch(device, data)
    done.value = `Отправлено на «${device.name}»`
  } catch (e) {
    error.value = (e as Error).message || 'Не удалось отправить'
  }
}
</script>

<template>
  <div v-if="open" class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4" @click.self="emit('close')">
    <div class="w-full max-w-md rounded-2xl bg-[var(--jb-surface,#1b1b1f)] p-5 shadow-xl">
      <div class="mb-4 flex items-center justify-between">
        <h3 class="text-lg font-semibold">В Лампе — запустить на ТВ</h3>
        <button class="opacity-60 hover:opacity-100" @click="emit('close')"><Icon name="close" /></button>
      </div>

      <p v-if="item" class="mb-4 truncate text-sm opacity-70">{{ item.title || item.name }}</p>

      <!-- Шаг 1: вход в cub.rip -->
      <div v-if="!cub.authorized()" class="space-y-3">
        <p class="text-sm opacity-80">
          Войдите в свою учётку CUB. Откройте на телефоне/ТВ
          <b>cub.rip/add</b> и введите показанный там код:
        </p>
        <input
          v-model="code"
          inputmode="numeric"
          placeholder="Код с cub.rip/add"
          class="w-full rounded-lg bg-black/30 px-3 py-2 outline-none"
          @keyup.enter="signIn"
        />
        <button
          class="w-full rounded-lg bg-[var(--jb-accent,#3b82f6)] px-3 py-2 font-medium disabled:opacity-50"
          :disabled="busy || !code.trim()"
          @click="signIn"
        >{{ busy ? 'Вход…' : 'Войти' }}</button>
      </div>

      <!-- Шаг 2+3: код терминала и выбор устройства -->
      <div v-else class="space-y-3">
        <label class="block text-sm opacity-80">
          Код терминала (задаётся в Лампе на ТВ: Настройки → Терминал):
          <input
            :value="cub.terminalCode.value"
            placeholder="Код терминала с ТВ"
            class="mt-1 w-full rounded-lg bg-black/30 px-3 py-2 outline-none"
            @input="cub.setTerminalCode(($event.target as HTMLInputElement).value)"
          />
        </label>

        <div class="text-sm opacity-70">
          Связь с CUB: {{ cub.socketState.value }}
          <button class="ml-2 underline opacity-70 hover:opacity-100" @click="cub.refreshDevices()">обновить устройства</button>
        </div>

        <!--
          Техданные. Пустой список устройств не отвечает на главный вопрос:
          сервер молчит или отвечает пустотой. Числа отвечают.
        -->
        <div class="text-xs opacity-50">
          сообщений от CUB: {{ cub.received.value }}
          <template v-if="cub.lastMethod.value"> · последнее: {{ cub.lastMethod.value }}</template>
        </div>

        <!--
          Два разных случая, а выглядели они одинаково: сокет не открылся вовсе
          — или открылся, но никто не ответил. Первое лечится на нашей стороне,
          второе настройками телевизора, поэтому разводим их прямо в подсказке.
        -->
        <div
          v-if="cub.devices.value.length === 0"
          class="rounded-lg bg-black/20 px-3 py-4 text-sm opacity-70"
        >
          <template v-if="cub.socketState.value !== 'open'">
            Нет связи с CUB — соединение не открылось. Телевизор тут ни при чём:
            либо браузер не пускает соединение, либо CUB не отвечает.
          </template>
          <template v-else>
            Связь с CUB есть, но ни одно устройство не отозвалось. Чаще всего
            это значит, что <b>телевизор не вошёл в тот же аккаунт CUB</b> —
            одного кода терминала для этого мало. В Лампе на телевизоре:
            Настройки → Аккаунт, войти под тем же аккаунтом, и отдельно
            Настройки → Терминал.
          </template>
        </div>
        <ul v-else class="space-y-2">
          <li v-for="d in cub.devices.value" :key="d.uid">
            <button
              class="flex w-full items-center justify-between rounded-lg bg-black/25 px-3 py-2 hover:bg-black/40 disabled:opacity-50"
              :disabled="!cub.terminalCode.value"
              @click="run(d)"
            >
              <span class="truncate">{{ d.name }}</span>
              <Icon name="play" />
            </button>
          </li>
        </ul>

        <button class="text-xs underline opacity-50 hover:opacity-80" @click="cub.logout()">выйти из CUB</button>
      </div>

      <p v-if="error" class="mt-3 text-sm text-red-400">{{ error }}</p>
      <p v-if="done" class="mt-3 text-sm text-green-400">{{ done }}</p>
    </div>
  </div>
</template>
