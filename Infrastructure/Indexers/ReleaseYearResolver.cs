using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Metadata;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Models.Api;

namespace JacBlack.Infrastructure.Indexers
{
    /// <summary>
    /// Достаёт год раздаче, у которой он не разобрался из названия.
    ///
    /// Зачем. Год в запросе — условие отбора, и раздача без года из карточки
    /// выбывает. Правило верное (иначе в карточку фильма лезут сериалы-тёзки,
    /// у которых год просто не разобрался), но у него оказалась цена: сценовые
    /// релизы год в названии не пишут ВООБЩЕ — «The.Boys.S05E08.1080p.WEB»,
    /// — и на этом из карточки «The Boys» вылетало 49 раздач piratebay из 51.
    /// То есть правило выкашивало не мусор, а целый класс источников.
    ///
    /// Поэтому отсев не смягчаем, а даём ему то, чего не хватало: год.
    /// Три источника, от точного к общему.
    ///
    /// 1. Сезон сериала. Самый точный и единственно верный для сериала:
    ///    карточка «The Boys» датирована 2019 годом, а пятый сезон вышел
    ///    в 2026-м. Годы сезонов приходят из TMDB одним запросом на карточку.
    /// 2. Код IMDB раздачи. Словарь знает год по коду — 403 970 записей.
    /// 3. Код Кинопоиска. Закрывает русское кино, где кода IMDB нет.
    ///
    /// Ничего не нашлось — год остаётся нулевым, и раздача выбывает, как
    /// раньше. Правило не ослаблено ни на шаг: оно просто перестало наказывать
    /// за наш собственный неполный разбор.
    ///
    /// Правим объект выдачи, а не запись в базе: `info` собирается заново на
    /// каждый ответ (IndexerSearchEngine), поэтому подставленный год уезжает
    /// в Лампу — и в отбор, и в показ, — но в базу не попадает. Это намеренно:
    /// в базе должно лежать то, что действительно написано в названии.
    /// </summary>
    public static class ReleaseYearResolver
    {
        /// <summary>
        /// Проставляет `info.relased` тем раздачам, где он нулевой.
        /// Возвращает, скольким год удалось добыть.
        /// </summary>
        /// <param name="cardImdb">Код карточки — по нему берутся годы сезонов.</param>
        /// <param name="cardIsSerial">Тип карточки в терминах Лампы: 2 — сериал.</param>
        public static int Fill(List<Result> results, string cardImdb, int cardIsSerial)
        {
            if (results == null || results.Count == 0)
                return 0;

            // Карту сезонов тянем только если она кому-то нужна: карточка
            // сериальная и есть хоть одна раздача без года. Иначе поиск платил
            // бы сетевым запросом за то, чем не пользуется.
            Dictionary<int, int> seasonYears = null;
            bool seasonsAsked = false;

            int filled = 0;

            foreach (var r in results)
            {
                var info = r?.info;
                if (info == null || info.relased > 0)
                    continue;

                int year = 0;

                if (cardIsSerial == 2 && info.seasons != null && info.seasons.Count > 0)
                {
                    if (!seasonsAsked)
                    {
                        seasonYears = TmdbSeasonYears.ByImdb(cardImdb);
                        seasonsAsked = true;
                    }

                    if (seasonYears != null)
                    {
                        // У сборника сезонов берём САМЫЙ РАННИЙ: раздача
                        // начинается с него, и он ближе всего к году карточки —
                        // а значит безопаснее для отбора, чем поздний.
                        // Нулевой сезон (спецвыпуски) пропускаем: его дата
                        // к сезонам отношения не имеет.
                        foreach (int s in info.seasons.Where(s => s > 0).OrderBy(s => s))
                        {
                            if (seasonYears.TryGetValue(s, out int sy) && sy > 0)
                            {
                                year = sy;
                                break;
                            }
                        }
                    }
                }

                if (year <= 0 && !string.IsNullOrEmpty(info.imdb)
                    && ImdbIndex.TryGet(info.imdb, out var it) && it != null && it.Year > 0)
                    year = it.Year;

                if (year <= 0 && !string.IsNullOrEmpty(info.kinopoisk)
                    && KinopoiskIndex.TryGet(info.kinopoisk, out var kt) && kt != null && kt.Year > 0)
                    year = kt.Year;

                if (year > 0)
                {
                    info.relased = year;
                    filled++;
                }
            }

            return filled;
        }
    }
}
