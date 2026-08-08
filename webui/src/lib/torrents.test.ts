import { describe, expect, it } from 'vitest'
import {
  applyClientFilters,
  buildFacets,
  formatDvLabel,
  mediaTokens,
  sortItems,
  torrentKey,
  type TorrentItem,
} from '@/lib/torrents'

const items: TorrentItem[] = [
  {
    tracker: 'one',
    title: 'Movie Director Cut',
    sid: 3,
    size: 10,
    voices: ['EN'],
    types: ['movie'],
    quality: 1080,
  },
  {
    tracker: 'two',
    title: 'Movie Dubbed',
    sid: 8,
    size: 5,
    voices: ['RU'],
    types: ['movie'],
    quality: 2160,
  },
]

describe('torrent collection helpers', () => {
  it('sorts without mutating input', () => {
    const sorted = sortItems(items, 'sid')
    expect(sorted.map((item) => item.sid)).toEqual([8, 3])
    expect(items[0]?.sid).toBe(3)
  })

  it('filters by include and exclude title fragments', () => {
    expect(
      applyClientFilters(items, 'movie', 'dubbed'),
    ).toEqual([items[0]])
  })

  it('builds stable facets and identities', () => {
    expect(buildFacets(items).tracker).toEqual(['one', 'two'])
    expect(buildFacets(items).quality).toEqual(['1080', '2160'])
    expect(torrentKey(items[0]!)).toContain('one')
  })
})

describe('Dolby Vision в подписи', () => {
  it('различает два значения, как Лампа', () => {
    expect(formatDvLabel('dv')).toBe('DV')
    expect(formatDvLabel('dvtv')).toBe('DV TV')
    expect(formatDvLabel('DVTV')).toBe('DV TV')
  })

  it('пусто, когда DV нет', () => {
    expect(formatDvLabel(null)).toBe('')
    expect(formatDvLabel('')).toBe('')
    expect(formatDvLabel('hdr')).toBe('')
  })
})

describe('плашки дорожек', () => {
  it('кодек видео идёт первым, дорожки не повторяются', () => {
    expect(
      mediaTokens({
        video: 'x265',
        tracks: [
          { codec: 'eac3', language: 'rus' },
          { codec: 'eac3', language: 'rus' },
          { codec: 'aac', language: 'eng' },
        ],
      }),
    ).toEqual(['x265', 'eac3 rus', 'aac eng'])
  })

  it('без разбора ffprobe показываем общий набор кодеков', () => {
    expect(mediaTokens({ video: 'x264', audio: ['ac3', 'aac'] })).toEqual(['x264', 'ac3', 'aac'])
  })

  it('пусто, когда сводки нет', () => {
    expect(mediaTokens(null)).toEqual([])
    expect(mediaTokens({})).toEqual([])
  })
})

