import path from 'node:path'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { loadEnv } from 'vite'
import { defineConfig } from 'vitest/config'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const api = env.VITE_API_PROXY_TARGET || 'http://localhost:9117'

  return {
    // Базовый путь здесь не задаём: приложение живёт в корне. Чтобы выложить
    // сборку на временный путь рядом с работающей, передаём флагом:
    //   npx vite build --base=/next/
    // Через переменную окружения не выходит — vite читает конфиг так, что
    // process.env до него не доносится, и база молча остаётся корневой.
    plugins: [vue(), tailwindcss()],
    resolve: {
      alias: { '@': path.resolve(__dirname, './src') },
    },
    server: {
      proxy: {
        '/api': api,
        '/stats/torrents': api,
        '/stats/meta': api,
        '/stats/tracks': api,
        '/health': api,
      },
    },
    build: {
      outDir: 'dist',
      emptyOutDir: true,
      // Оболочки уже разнесены динамическим импортом; отдельная нарезка
      // вендора не нужна — зависимостей осталось шесть.
    },
    test: {
      environment: 'happy-dom',
      include: ['src/**/*.{test,spec}.ts'],
    },
  }
})
