import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

/**
 * Жив ли сервер. Нужен для точки состояния в шапке.
 *
 * Спрашиваем раз в минуту и не повторяем при неудаче: если сервер лёг,
 * долбить его повторами бессмысленно, а показать «нет связи» надо сразу.
 */
export function useHealth() {
  const query = useQuery({
    queryKey: ['health'],
    staleTime: 30_000,
    refetchInterval: 60_000,
    retry: false,
    queryFn: async ({ signal }) => {
      const res = await fetch('/health', { signal, headers: { accept: 'application/json' } })
      if (!res.ok) throw new Error(String(res.status))
      return true
    },
  })

  return {
    isOnline: computed(() => query.isSuccess.value && !query.isError.value),
    isChecking: computed(() => query.isLoading.value),
  }
}
