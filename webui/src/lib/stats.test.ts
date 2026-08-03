import { describe, expect, it } from 'vitest'
import {
  aggregateTrackers,
  filterAndSortTrackers,
  formatDuration,
  formatSilence,
  formatStatNumber,
  getTracksData,
  type TrackerStat,
} from '@/lib/stats'

const rows: TrackerStat[] = [
  {
    trackerName: 'rutor',
    newtor: 2,
    alltorrents: 10,
    tracks: { confirm: 3, wait: 1, skip: 0 },
  },
  {
    trackerName: 'kinozal',
    newtor: 5,
    alltorrents: 20,
    tracks: { confirm: 4, wait: 0, skip: 2 },
  },
]

describe('stats helpers', () => {
  it('normalizes missing track counters', () => {
    expect(getTracksData({ trackerName: 'x' })).toEqual({
      confirm: 0,
      wait: 0,
      skip: 0,
    })
  })

  it('filters, sorts and aggregates tracker rows', () => {
    expect(filterAndSortTrackers(rows, '', 'newtor')[0]?.trackerName).toBe(
      'kinozal',
    )
    expect(filterAndSortTrackers(rows, 'rutor', 'name')).toHaveLength(1)
    expect(aggregateTrackers(rows)).toMatchObject({
      newtor: 7,
      alltorrents: 30,
      confirm: 7,
      wait: 1,
      skip: 2,
    })
  })

  it('formats compact values', () => {
    expect(formatStatNumber(1_500, false, 'en')).toBe('1.5K')
  })
})

describe('formatSilence', () => {
  it('без данных не выдумывает срок', () => {
    expect(formatSilence(null).text).toBe('—')
    expect(formatSilence(undefined).alarming).toBe(false)
  })

  it('свежее говорит словами, а не числом', () => {
    expect(formatSilence(0).text).toBe('сегодня')
    expect(formatSilence(1).text).toBe('вчера')
  })

  // Порог тревоги — неделя: у сериалов и аниме недельный ритм, поэтому
  // семь суток это норма, а восемь уже повод посмотреть.
  it('тревожит только после недели молчания', () => {
    expect(formatSilence(7).alarming).toBe(false)
    expect(formatSilence(8).alarming).toBe(true)
    expect(formatSilence(87).text).toBe('87 сут')
  })
})

describe('formatDuration', () => {
  it('часы, а не сотни минут', () => {
    expect(formatDuration(523)).toBe('8 ч 43 мин')
    expect(formatDuration(600)).toBe('10 ч')
    expect(formatDuration(45)).toBe('45 мин')
    expect(formatDuration(0)).toBe('меньше минуты')
  })
})
