import { describe, expect, it } from 'vitest'
import {
  countActive,
  countAlive,
  countFacet,
  applyFilters,
  EMPTY_CLIENT_FILTERS,
  matches,
  sizeBucketKey,
  type ClientFilters,
} from '@/lib/filters'
import type { TorrentItem } from '@/lib/torrents'

const GB = 1024 ** 3

function t(over: Partial<TorrentItem> = {}): TorrentItem {
  return {
    tracker: 'rutor',
    title: 'Дюна',
    quality: 1080,
    relased: 2024,
    size: 8 * GB,
    sid: 10,
    videotype: 'sdr',
    voices: [],
    types: ['movie'],
    ...over,
  }
}

function f(over: Partial<ClientFilters> = {}): ClientFilters {
  return { ...EMPTY_CLIENT_FILTERS, ...over }
}

describe('ступени размера', () => {
  it.each([
    [0.5, 'xs'],
    [1.96, 'xs'],
    [3.17, 's'],
    [8.74, 'm'],
    [14.29, 'l'],
    [48.45, 'xl'],
    [110.61, 'xxl'],
  ])('%s ГБ попадает в ступень %s', (gb, key) => {
    expect(sizeBucketKey(gb * GB)).toBe(key)
  })

  it('раздача без размера не попадает никуда', () => {
    expect(sizeBucketKey(0)).toBe('')
    expect(sizeBucketKey(null)).toBe('')
  })

  it('границы принадлежат верхней ступени', () => {
    // 2 ГБ — это уже «2–5», а не «до 2»: иначе раздача попала бы в обе.
    expect(sizeBucketKey(2 * GB)).toBe('s')
    expect(sizeBucketKey(50 * GB)).toBe('xxl')
  })
})

describe('отбор', () => {
  it('пустой фильтр пропускает всё', () => {
    expect(matches(t(), f())).toBe(true)
  })

  it('несколько значений в группе работают как «или»', () => {
    const filters = f({ quality: ['1080', '2160'] })
    expect(matches(t({ quality: 1080 }), filters)).toBe(true)
    expect(matches(t({ quality: 2160 }), filters)).toBe(true)
    expect(matches(t({ quality: 720 }), filters)).toBe(false)
  })

  it('разные группы работают как «и»', () => {
    const filters = f({ quality: ['1080'], tracker: ['rutor'] })
    expect(matches(t({ quality: 1080, tracker: 'rutor' }), filters)).toBe(true)
    expect(matches(t({ quality: 1080, tracker: 'kinozal' }), filters)).toBe(false)
  })

  it('раздача без значения не проходит явный выбор', () => {
    // Год неизвестен — под «2024» такая раздача не подходит, и показывать
    // её среди отобранных нельзя: человек просил именно 2024-й.
    expect(matches(t({ relased: null }), f({ year: ['2024'] }))).toBe(false)
  })

  it('живой считается любая раздача с сидами, подтверждена или нет', () => {
    expect(matches(t({ sid: 1 }), f({ aliveOnly: true }))).toBe(true)
    expect(matches(t({ sid: 0 }), f({ aliveOnly: true }))).toBe(false)
  })

  it('HDR отбирается отдельным признаком', () => {
    expect(matches(t({ videotype: 'hdr' }), f({ hdr: true }))).toBe(true)
    expect(matches(t({ videotype: 'sdr' }), f({ hdr: true }))).toBe(false)
  })

  it('уточнение и исключение смотрят в заголовок', () => {
    expect(matches(t({ title: 'Дюна BDRemux' }), f({ refine: 'remux' }))).toBe(true)
    expect(matches(t({ title: 'Дюна BDRip' }), f({ refine: 'remux' }))).toBe(false)
    expect(matches(t({ title: 'Дюна CAMRip' }), f({ exclude: 'cam' }))).toBe(false)
  })

  it('озвучек у раздачи может быть несколько', () => {
    const item = t({ voices: ['LostFilm', 'Jaskier'] })
    expect(matches(item, f({ voice: ['Jaskier'] }))).toBe(true)
    expect(matches(item, f({ voice: ['Кубик в Кубе'] }))).toBe(false)
  })
})

describe('счётчики фасетов', () => {
  const items = [
    t({ tracker: 'rutor', quality: 1080 }),
    t({ tracker: 'rutor', quality: 480 }),
    t({ tracker: 'kinozal', quality: 1080 }),
    t({ tracker: 'kinozal', quality: 2160 }),
  ]

  it('без фильтров считает всю выдачу', () => {
    expect(countFacet(items, f(), 'tracker')).toEqual([
      { value: 'kinozal', count: 2 },
      { value: 'rutor', count: 2 },
    ])
  })

  it('своя группа из подсчёта исключается', () => {
    // Это главное свойство: выбрав rutor, человек должен видеть, сколько
    // получит, если переключится на kinozal. Считай мы по отфильтрованному,
    // у kinozal стоял бы ноль — счётчики вводили бы в заблуждение.
    const counts = countFacet(items, f({ tracker: ['rutor'] }), 'tracker')
    expect(counts).toEqual([
      { value: 'kinozal', count: 2 },
      { value: 'rutor', count: 2 },
    ])
  })

  it('чужие группы подсчёт сужают', () => {
    const counts = countFacet(items, f({ tracker: ['rutor'] }), 'quality')
    expect(counts).toEqual([
      { value: '1080', count: 1 },
      { value: '480', count: 1 },
    ])
  })

  it('качество и год идут по убыванию, а не по алфавиту', () => {
    const counts = countFacet(items, f(), 'quality')
    expect(counts.map((c) => c.value)).toEqual(['2160', '1080', '480'])
  })

  it('ступени размера сохраняют свой порядок', () => {
    const sized = [t({ size: 60 * GB }), t({ size: 1 * GB }), t({ size: 7 * GB })]
    expect(countFacet(sized, f(), 'size').map((c) => c.value)).toEqual(['xs', 'm', 'xxl'])
  })
})

describe('сводные числа', () => {
  it('считает применённые группы, а не значения', () => {
    expect(countActive(f())).toBe(0)
    expect(countActive(f({ quality: ['1080', '2160'] }))).toBe(1)
    expect(countActive(f({ quality: ['1080'], tracker: ['rutor'], aliveOnly: true }))).toBe(3)
    expect(countActive(f({ refine: '   ' }))).toBe(0)
  })

  it('считает живых', () => {
    expect(countAlive([t({ sid: 5 }), t({ sid: 0 }), t({ sid: null })])).toBe(1)
  })

  it('отбор возвращает подходящее', () => {
    const list = [t({ quality: 1080 }), t({ quality: 480 })]
    expect(applyFilters(list, f({ quality: ['1080'] }))).toHaveLength(1)
  })
})

describe('сезоны', () => {
  it('раздача подходит под выбранный сезон', () => {
    expect(matches(t({ seasons: [3] }), f({ season: ['3'] }))).toBe(true)
    expect(matches(t({ seasons: [2] }), f({ season: ['3'] }))).toBe(false)
  })

  it('сборник сезонов подходит под любой из своих', () => {
    // «Пацаны (1-3 сезоны)» — одна раздача, три сезона.
    const пакет = t({ seasons: [1, 2, 3] })

    expect(matches(пакет, f({ season: ['1'] }))).toBe(true)
    expect(matches(пакет, f({ season: ['3'] }))).toBe(true)
    expect(matches(пакет, f({ season: ['4'] }))).toBe(false)
  })

  it('фильм под явный выбор сезона не подходит', () => {
    expect(matches(t({ seasons: [] }), f({ season: ['1'] }))).toBe(false)
    expect(matches(t({ seasons: null }), f({ season: ['1'] }))).toBe(false)
  })

  it('без выбора сезона ничего не отсекается', () => {
    expect(matches(t({ seasons: [] }), f())).toBe(true)
    expect(matches(t({ seasons: [5] }), f())).toBe(true)
  })

  it('нулевой сезон спецвыпусков в список не идёт', () => {
    const facets = countFacet([t({ seasons: [0, 2] })], f(), 'season')

    expect(facets.map((x) => x.value)).toEqual(['2'])
  })

  it('сезоны идут по возрастанию, а не по числу раздач', () => {
    const items = [
      t({ seasons: [5] }),
      t({ seasons: [5] }),
      t({ seasons: [5] }),
      t({ seasons: [1] }),
      t({ seasons: [3] }),
    ]

    expect(countFacet(items, f(), 'season').map((x) => x.value)).toEqual(['1', '3', '5'])
  })
})
