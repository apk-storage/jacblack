using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Persistence;
using JacBlack.Infrastructure.Utils;

namespace JacBlack.Infrastructure.Trackers.Kinozal
{
    /// <summary>
    /// Наполняет словарь кодов Кинопоиска впрок, обходя базу.
    ///
    /// Зачем отдельно от <see cref="KinozalKinopoiskHarvester"/>. Тот добывает
    /// код по факту поиска — по одной карточке, и только той, которую человек
    /// открыл. Словарь так растёт медленно, а разводить тёзок он может лишь
    /// там, где код уже добыт. Здесь обратный ход: берём то, что в базе давно
    /// лежит, и добираем коды заранее.
    ///
    /// Почему источник один. Ссылку на Кинопоиск публикует kinozal; остальные
    /// трекеры её не дают. Но фильм один и тот же, поэтому один поход за
    /// страницей закрывает карточку целиком — код потом разъезжается по
    /// раздачам всех трекеров миграцией fillKinopoiskFromDictionary.
    ///
    /// Как бережём трекер. Ходим строго по одному, с паузой, и НЕ начинаем,
    /// пока идёт обход kinozal: у него одна сессия на всех, и толкаться в ней
    /// нельзя. Работа ограничена сверху числом страниц за запуск — чтобы
    /// ночной прогон не превращался в многочасовой долбёж.
    /// </summary>
    public static class KinopoiskDictionaryHarvester
    {
        static readonly SemaphoreSlim _run = new SemaphoreSlim(1, 1);

        static readonly Regex KinozalId = new Regex(@"details\.php\?id=(\d+)", RegexOptions.Compiled);

        static volatile string _state = "не запускался";
        static int _asked, _got, _left;

        /// <summary>Что делает наполнитель прямо сейчас — для /stats и дежурного.</summary>
        public static object Snapshot() => new
        {
            состояние = _state,
            спрошено = _asked,
            добыто = _got,
            осталосьВОчереди = _left,
            кодовВСловаре = KinopoiskIndex.Count
        };

        /// <summary>
        /// Запускает разовый проход в фоне. Повторный вызов, пока идёт первый,
        /// ничего не делает — иначе два прохода полезли бы в одну сессию.
        /// </summary>
        /// <param name="limit">Сколько страниц взять за прогон.</param>
        /// <param name="delayMs">Пауза между страницами.</param>
        public static object Start(KinozalSyncService kinozal, int limit, int delayMs)
        {
            if (kinozal == null)
                return new { ok = false, ошибка = "нет службы kinozal" };

            if (!_run.Wait(0))
                return new { ok = false, ошибка = "проход уже идёт", состояние = Snapshot() };

            _ = Task.Run(async () =>
            {
                try
                {
                    await Harvest(kinozal, limit, delayMs);
                }
                catch (Exception ex)
                {
                    _state = "прерван ошибкой";
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers,
                        "kinopoisk: наполнение словаря прервано", ex);
                }
                finally
                {
                    _run.Release();
                }
            });

            return new { ok = true, запущено = true, лимит = limit, пауза = delayMs };
        }

        static async Task Harvest(KinozalSyncService kinozal, int limit, int delayMs)
        {
            // Пока идёт обход kinozal, не лезем: сессия одна.
            var crawl = Indexers.CrawlProgress.Snapshot();
            if (crawl.TryGetValue("kinozal", out var run) && run.FinishedAt == null)
            {
                _state = "отложен: идёт обход kinozal";
                JacBlackLog.Information(JacBlackLogCategories.Trackers,
                    "kinopoisk: наполнение словаря отложено — идёт обход kinozal");
                return;
            }

            _state = "выбираю карточки";
            _asked = 0;
            _got = 0;

            var queue = CollectCandidates(limit);
            _left = queue.Count;

            JacBlackLog.Information(JacBlackLogCategories.Trackers,
                $"kinopoisk: наполнение словаря — карточек в очереди {queue.Count}");

            _state = "хожу за страницами";

            foreach (var card in queue)
            {
                // Пока шли по очереди, обход мог начаться — уступаем ему.
                crawl = Indexers.CrawlProgress.Snapshot();
                if (crawl.TryGetValue("kinozal", out run) && run.FinishedAt == null)
                {
                    _state = "остановлен: начался обход kinozal";
                    break;
                }

                _asked++;
                _left--;

                try
                {
                    string code = await kinozal.GetKinopoiskIdAsync(card.KinozalId);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        KinopoiskIndex.Remember(code, card.Name, card.OriginalName, card.Year);
                        _got++;
                    }
                }
                catch (Exception ex)
                {
                    JacBlackLog.Swallowed(JacBlackLogCategories.Trackers,
                        $"kinopoisk: код для «{card.Name}» ({card.Year}) не добыт", ex);
                }

                // Сохраняем не после каждой страницы, а пачками: словарь
                // целиком переписывается, и на каждой странице это лишняя
                // работа. Но и не в самом конце — прогон могут прервать.
                if (_got > 0 && _asked % 25 == 0)
                    KinopoiskIndex.SaveIfDirty(force: true);

                if (delayMs > 0)
                    await Task.Delay(delayMs);
            }

            KinopoiskIndex.SaveIfDirty(force: true);

            _state = $"закончил: спрошено {_asked}, добыто {_got}";
            JacBlackLog.Information(JacBlackLogCategories.Trackers,
                $"kinopoisk: наполнение словаря закончено — спрошено {_asked}, добыто {_got}, всего в словаре {KinopoiskIndex.Count}");
        }

        sealed class Card
        {
            public string KinozalId;
            public string Name;
            public string OriginalName;
            public int Year;
        }

        /// <summary>
        /// Кого спрашивать. Берём раздачи kinozal без кода, с годом, по одной
        /// на карточку — код нужен один на вещь, а раздач у неё десятки.
        /// </summary>
        static List<Card> CollectCandidates(int limit)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Card>();

            foreach (var item in FileDB.masterDb.ToArray())
            {
                if (result.Count >= limit)
                    break;

                var rows = FileDB.OpenRead(item.Key, cache: false);
                if (rows == null)
                    continue;

                foreach (var kv in rows)
                {
                    if (result.Count >= limit)
                        break;

                    var t = kv.Value;
                    if (t == null || t.relased <= 1900)
                        continue;

                    if (!string.Equals(t.trackerName, "kinozal", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.IsNullOrWhiteSpace(t.kinopoisk))
                        continue;

                    // Уже знаем эту вещь — ходить незачем.
                    if (KinopoiskIndex.TryGetByTitle(t.originalname, t.relased, out _)
                        || KinopoiskIndex.TryGetByTitle(t.name, t.relased, out _))
                        continue;

                    var m = KinozalId.Match(t.url ?? "");
                    if (!m.Success)
                        continue;

                    string key = $"{StringConvert.SearchName(t.name)}:{t.relased}";
                    if (!seen.Add(key))
                        continue;

                    result.Add(new Card
                    {
                        KinozalId = m.Groups[1].Value,
                        Name = t.name,
                        OriginalName = t.originalname,
                        Year = t.relased
                    });
                }
            }

            return result;
        }
    }
}
