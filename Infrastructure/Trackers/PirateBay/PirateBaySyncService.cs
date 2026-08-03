using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Networking;
using JacBlack.Infrastructure.Parsing;
using JacBlack.Infrastructure.Persistence;
using Newtonsoft.Json;

namespace JacBlack.Infrastructure.Trackers.PirateBay
{
    /// <summary>
    /// Обход The Pirate Bay через открытое API apibay.org.
    ///
    /// Берём готовые списки «сотня самых раздаваемых» по каждому разделу:
    /// именно ради них источник и заводился. Замер 29.07.2026: в разделе
    /// HD-фильмов медиана 1268 сидов, максимум 10 140 — на два порядка больше
    /// типичных значений у русских трекеров.
    ///
    /// Поиск по запросу API тоже умеет, но для наполнения базы он хуже:
    /// свежие раздачи там почти без сидов, а нам нужны живые.
    /// </summary>
    public class PirateBaySyncService
    {
        const string TrackerName = "piratebay";
        const int RequestDelayMs = 1200;

        /// <summary>
        /// Пауза глубокого обхода. Меньше обычной: ему идти сотнями тысяч
        /// запросов, и на 1.2 с круг растянулся бы на два месяца. Замер
        /// 03.08.2026: двадцать запросов подряд с паузой 0.4 с прошли все
        /// двадцать, отказов нет. Берём 0.6 с — вдвое быстрее прежнего и
        /// в полтора раза осторожнее измеренного предела.
        /// </summary>
        const int DeepRequestDelayMs = 600;

        static readonly TrackerParseLock _parseLock = new TrackerParseLock();

        /// <summary>
        /// Глубокий обход идёт часами, поэтому у него свой признак занятости —
        /// иначе очередной запуск по расписанию налез бы на текущий.
        /// </summary>
        static readonly TrackerWorkFlag _parseAllTaskWork = new TrackerWorkFlag();

        static string Host => (AppInit.conf.PirateBay?.host ?? "https://apibay.org").TrimEnd('/');

        /// <summary>Разделы: фильмы, фильмы DVDR, сериалы, документальные, HD-фильмы, HD-сериалы, 3D.</summary>
        static readonly string[] Categories = { "207", "208", "201", "205", "202", "206", "209" };

        public async Task<string> ParseAsync(CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                var sw = Stopwatch.StartNew();
                ParserLog.Write(TrackerName, $"Parse start, разделов {Categories.Length}, host={Host}");

                int fetched = 0, accepted = 0;

                try
                {
                    foreach (string category in Categories)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string url = $"{Host}/precompiled/data_top100_{category}.json";
                        string json = await HttpClient.Get(url, timeoutSeconds: 20, useproxy: AppInit.conf.PirateBay.useproxy);

                        if (string.IsNullOrWhiteSpace(json))
                        {
                            ParserLog.Write(TrackerName, $"Раздел {category}: пустой ответ");
                            continue;
                        }

                        List<PirateBayItem> items;
                        try { items = JsonConvert.DeserializeObject<List<PirateBayItem>>(json); }
                        catch (JsonException ex)
                        {
                            ParserLog.Write(TrackerName, $"Раздел {category}: ответ не разобран — {ex.Message}");
                            continue;
                        }

                        if (items == null || items.Count == 0)
                            continue;

                        fetched += items.Count;

                        var torrents = PirateBayParser.ParseItems(items);
                        if (torrents.Count > 0)
                        {
                            FileDB.AddOrUpdate(torrents);
                            accepted += torrents.Count;
                        }

                        ParserLog.Write(TrackerName, $"Раздел {category} | в ответе {items.Count}, принято {torrents.Count}");

                        await Task.Delay(RequestDelayMs, cancellationToken);
                    }

                    string log = $"разделов={Categories.Length}, в ответах={fetched}, принято={accepted}";
                    ParserLog.Write(TrackerName, $"Parse completed successfully (took {sw.Elapsed.TotalSeconds:F1}s) | {log}");
                    return log;
                }
                catch (OperationCanceledException)
                {
                    ParserLog.Write(TrackerName, "Parse cancelled");
                    return "cancelled";
                }
                catch (Exception ex)
                {
                    ParserLog.Write(TrackerName, $"Error: {ex.Message}");
                    return $"error: {ex.Message}";
                }
            });
        }

        /// <summary>
        /// Глубокий обход: спрашиваем TPB о том, что уже знаем сами.
        ///
        /// Почему не перебором страниц. У apibay постраничности НЕТ — проверено
        /// 03.08.2026: `page=1` и `page=2` отдают одно и то же, параметр просто
        /// игнорируется. Списки «сотня самых раздаваемых» дают ровно 700 записей
        /// на семь разделов, и это потолок по построению: сколько ни запускай,
        /// новых взяться неоткуда. Оттого в базе за всё время и накопилось 776
        /// раздач при миллионе с лишним у остальных источников, а суточная
        /// сводка показывала 31 489 «обновлённых» — те же семьсот, перечитанные
        /// сорок пять раз.
        ///
        /// Зато поиск отвечает сотней раздач на запрос. Запросы берём из своего
        /// словаря кодов IMDB: там 75 тысяч оригинальных названий, собранных из
        /// yts, eztv и страниц rutracker. TPB англоязычный, и такие названия он
        /// понимает — проверено на живых запросах, «The Mandalorian», «Silo»,
        /// «Dune» вернули по сотне.
        ///
        /// Побочная польза важнее прямой: мы не просто набираем объём, а
        /// добираем ИМЕННО те раздачи, которые лягут в уже существующие карточки
        /// рядом с русскими. Для Лампы это лишний источник с сидами на два
        /// порядка выше.
        ///
        /// Обход возобновляемый: положение в словаре хранится в файле, как
        /// страница у bitru. Прервали — следующий заход продолжит с того же
        /// места, а не начнёт круг заново.
        /// </summary>
        public async Task<string> ParseAllTaskAsync(int maxQueries = 20000, CancellationToken cancellationToken = default)
        {
            return await TrackerSyncHelpers.RunParseAllTaskAsync(TrackerName, _parseAllTaskWork, checkDisabled: true, async () =>
            {
                var titles = ImdbIndex.AllTitles();
                if (titles.Count == 0)
                {
                    ParserLog.Write(TrackerName, "глубокий обход: словарь названий пуст, идти не с чем");
                    return;
                }

                var sw = Stopwatch.StartNew();
                int cursor = ReadCursor();
                if (cursor >= titles.Count)
                    cursor = 0;

                ParserLog.Write(TrackerName,
                    $"глубокий обход: названий {titles.Count}, продолжаю с {cursor}");

                int asked = 0, fetched = 0, saved = 0, empty = 0, skipped = 0;

                try
                {
                    while (asked < maxQueries && cursor < titles.Count)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var (title, year) = titles[cursor];
                        cursor++;

                        // Кириллицу и иероглифы не спрашиваем вовсе: TPB
                        // англоязычный, такой запрос гарантированно вернёт
                        // пустоту. После вливания выгрузки IMDb в словаре
                        // 56 тысяч русских названий — это 19 часов запросов
                        // впустую, если их не отсеять.
                        if (!LooksLatin(title))
                        {
                            skipped++;
                            SaveCursor(cursor);
                            continue;
                        }

                        asked++;

                        string url = $"{Host}/q.php?q={Uri.EscapeDataString(title)}&cat=200";
                        string json = await HttpClient.Get(url, timeoutSeconds: 20, useproxy: AppInit.conf.PirateBay.useproxy);

                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            List<PirateBayItem> items = null;
                            try { items = JsonConvert.DeserializeObject<List<PirateBayItem>>(json); }
                            catch (JsonException) { }

                            // Пустой ответ API возвращает не пустым списком, а
                            // одной записью с нулевым идентификатором.
                            if (items != null && items.Count > 0 && items[0]?.Id != "0")
                            {
                                fetched += items.Count;

                                var torrents = PirateBayParser.ParseItems(items);
                                if (torrents.Count > 0)
                                {
                                    FileDB.AddOrUpdate(torrents);
                                    saved += torrents.Count;
                                }
                            }
                            else
                            {
                                empty++;
                            }
                        }

                        SaveCursor(cursor);

                        if (asked % 25 == 0)
                            ParserLog.Write(TrackerName,
                                $"глубокий обход: запросов {asked}, в ответах {fetched}, сохранено {saved}, " +
                                $"пустых {empty}, пропущено нелатинских {skipped}, " +
                                $"положение {cursor}/{titles.Count}, идёт {sw.Elapsed.TotalMinutes:F0} мин");

                        await Task.Delay(DeepRequestDelayMs, cancellationToken);
                    }

                    if (cursor >= titles.Count)
                    {
                        ParserLog.Write(TrackerName, "глубокий обход: словарь пройден целиком, начинаем круг заново");
                        SaveCursor(0);
                    }

                    ParserLog.Write(TrackerName,
                        $"глубокий обход завершён | запросов={asked}, в ответах={fetched}, сохранено={saved}, " +
                        $"пустых={empty}, пропущено нелатинских={skipped}, заняло={sw.Elapsed.TotalMinutes:F1} мин");
                }
                catch (OperationCanceledException)
                {
                    ParserLog.Write(TrackerName,
                        $"глубокий обход прерван | запросов={asked}, сохранено={saved}, положение {cursor}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Написано ли название латиницей. Диакритика допускается («Amélie»,
        /// «Kærlighed»), кириллица, греческий и иероглифы — нет: латинские
        /// блоки Юникода заканчиваются на 0x024F, дальше начинаются чужие.
        /// </summary>
        static bool LooksLatin(string title)
        {
            // Явным кодом точки, а не самим знаком: буква U+024F в исходнике
            // неотличима от опечатки и переживает не всякую перекодировку.
            const char lastLatin = 'ɏ';

            bool hasLetter = false;

            foreach (char c in title)
            {
                if (!char.IsLetter(c))
                    continue;

                if (c > lastLatin)
                    return false;

                hasLetter = true;
            }

            return hasLetter;
        }

        static string CursorPath => "Data/temp/piratebay_query_cursor.txt";

        static int ReadCursor()
        {
            try
            {
                return System.IO.File.Exists(CursorPath)
                    && int.TryParse(System.IO.File.ReadAllText(CursorPath).Trim(), out int v) && v >= 0 ? v : 0;
            }
            catch (System.IO.IOException)
            {
                return 0;
            }
        }

        static void SaveCursor(int value)
        {
            try
            {
                System.IO.Directory.CreateDirectory("Data/temp");
                System.IO.File.WriteAllText(CursorPath, value.ToString());
            }
            catch (System.IO.IOException)
            {
                // Не записалось — следующий заход начнёт сначала. Повтор просто
                // обновит уже известное, потери данных нет.
            }
        }

        /// <summary>
        /// Точечный поиск по названию. Пригодится, когда в базе чего-то нет,
        /// а на TPB оно есть — например, свежий зарубежный релиз.
        /// </summary>
        public async Task<string> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "нужен запрос";

            return await TrackerSyncHelpers.RunParseAsync(TrackerName, _parseLock, checkDisabled: true, async () =>
            {
                string url = $"{Host}/q.php?q={Uri.EscapeDataString(query)}&cat=200";
                string json = await HttpClient.Get(url, timeoutSeconds: 20, useproxy: AppInit.conf.PirateBay.useproxy);

                if (string.IsNullOrWhiteSpace(json))
                    return "пустой ответ";

                var items = JsonConvert.DeserializeObject<List<PirateBayItem>>(json);
                var torrents = PirateBayParser.ParseItems(items);

                if (torrents.Count > 0)
                    FileDB.AddOrUpdate(torrents);

                ParserLog.Write(TrackerName, $"Поиск «{query}»: в ответе {items?.Count ?? 0}, принято {torrents.Count}");
                return $"найдено={items?.Count ?? 0}, принято={torrents.Count}";
            });
        }
    }
}
