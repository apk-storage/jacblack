using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;

namespace JacRed.Application.Search
{
    /// <summary>
    /// Одна раздача глазами живого опроса: с какого она трекера, какие у неё
    /// адреса и куда положить свежие счётчики.
    ///
    /// Нужна потому, что выдачу собирают ДВА разных пути с разными моделями:
    /// Лампа ходит в индексаторы (Result), сайт — в /api/v1.0/torrents
    /// (TorrentDetails). Общий слой работает с этой обёрткой и не знает, чью
    /// запись обновляет.
    /// </summary>
    public sealed class SeedTarget
    {
        /// <summary>Ключ записи — её основной адрес. По нему потом узнаём, кого проверили.</summary>
        public string Key { get; init; }

        /// <summary>Имя трекера. У склеенной записи это перечень через запятую.</summary>
        public string Tracker { get; init; }

        /// <summary>Все адреса записи одной строкой: основной плюс адреса склеенных копий.</summary>
        public string Urls { get; init; }

        /// <summary>Проставить живые счётчики.</summary>
        public Action<int, int> Apply { get; init; }
    }

    /// <summary>
    /// Живые сиды у закрытых трекеров.
    ///
    /// Почему отдельным слоем. Опрос анонса у них не работает: анонс не
    /// отвечает посторонним, а торрент-файлы помечены private, из-за чего
    /// нет ни DHT, ни обмена пирами. Зато у каждого есть свой способ:
    /// у nnmclub, kinozal и toloka — собственный поиск с колонкой сидов,
    /// у bitru — API по идентификатору, у rutracker — поиск через браузер.
    ///
    /// Почему ОБЩИЙ. Раньше этот слой висел только на пути Лампы, а сайт
    /// ходил другой ручкой — и показывал числа из базы. Из-за этого удалённая
    /// раздача «Кода 8» оставалась на сайте первой строкой с 96 раздающими,
    /// когда в Лампе она уже была исправлена (случай 02.08.2026). Две копии
    /// одной логики расходятся всегда; здесь копия одна.
    /// </summary>
    public sealed class ClosedTrackerSeeders
    {
        readonly Infrastructure.Trackers.Kinozal.KinozalSyncService _kinozal;
        readonly Infrastructure.Trackers.Toloka.TolokaSyncService _toloka;
        readonly Infrastructure.Trackers.Bitru.BitruApiSyncService _bitru;

        /// <summary>
        /// Сколько ждать трекеры, которые спрашиваются прямо в запросе.
        /// По очереди было нельзя: три поиска под входом складывались в 8–15
        /// секунд ответа. Кто не успел — его числа останутся из базы.
        /// </summary>
        static readonly TimeSpan InlineBudget = TimeSpan.FromSeconds(6);

        public ClosedTrackerSeeders(
            Infrastructure.Trackers.Kinozal.KinozalSyncService kinozal = null,
            Infrastructure.Trackers.Toloka.TolokaSyncService toloka = null,
            Infrastructure.Trackers.Bitru.BitruApiSyncService bitru = null)
        {
            _kinozal = kinozal;
            _toloka = toloka;
            _bitru = bitru;
        }

        /// <summary>
        /// Обновляет счётчики и возвращает ключи тех записей, чьи числа
        /// действительно проверены сейчас.
        ///
        /// Спрашиваем ОБОИМИ названиями карточки. Оригинальное обязательно:
        /// у трекера своё написание перевода, и по русскому листинг часто
        /// пуст — у «Мандалорца» так оставались непроверенными 35 раздач.
        /// Русское тоже нужно: слишком общий оригинал вроде «The Boys»
        /// топит свои же раздачи среди чужих строк.
        /// </summary>
        public async Task<HashSet<string>> ApplyAsync(
            IReadOnlyList<SeedTarget> targets,
            string originalTitle,
            string russianTitle,
            CancellationToken ct = default)
        {
            var verified = new HashSet<string>(StringComparer.Ordinal);

            if (targets == null || targets.Count == 0)
                return verified;

            string primary = FirstNotEmpty(originalTitle, russianTitle);
            if (string.IsNullOrWhiteSpace(primary))
                return verified;

            // Спрашиваем прямо в запросе только тех, кто отвечает быстро.
            var inline = new[]
            {
                ApplyNNMClubAsync(targets, primary, verified),
                ApplyKinozalAsync(targets, verified, primary, russianTitle)
            };

            try
            {
                await Task.WhenAll(inline).WaitAsync(InlineBudget, ct);
            }
            catch (Exception)
            {
                // Предел вышел или трекер не ответил — выдача уже собрана,
                // числа просто останутся из базы и будут помечены непроверенными.
            }

            // Эти спрашиваются ПОСЛЕ ответа: вход у них занимает секунды.
            // Берём сохранённое, обновление уходит в фон.
            ApplyTolokaCached(targets, primary, verified);
            ApplyBitruCached(targets, primary, verified);
            ApplyRutrackerCached(targets, verified, primary, russianTitle);

            return verified;
        }

        static string FirstNotEmpty(params string[] values) =>
            values?.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        /// <summary>
        /// Одна раздача бывает объединена из нескольких трекеров, и тогда в
        /// поле трекера лежит перечень через запятую. Точное сравнение такие
        /// записи пропускало: из 77 раздач по карточке «Извне» 14 оставались
        /// без живого опроса именно поэтому.
        /// </summary>
        static bool FromTracker(SeedTarget t, string tracker) =>
            !string.IsNullOrEmpty(t?.Tracker)
            && t.Tracker.Contains(tracker, StringComparison.OrdinalIgnoreCase);

        static bool Any(IReadOnlyList<SeedTarget> targets, string tracker)
        {
            foreach (var t in targets)
            {
                if (FromTracker(t, tracker))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Проставляет счётчики и возвращает раздачи, которых поиск трекера не
        /// вернул. Само по себе это НЕ значит «удалена»: строка могла не
        /// попасть в первые страницы или не совпасть по названию. Это лишь
        /// список кандидатов, которых потом спрашивают поштучно.
        /// </summary>
        static List<string> Fill(
            IReadOnlyList<SeedTarget> targets,
            string tracker,
            string idPattern,
            Func<string, (int sid, int pir)?> lookup,
            HashSet<string> verified)
        {
            var missing = new List<string>();

            foreach (var t in targets)
            {
                if (!FromTracker(t, tracker))
                    continue;

                var m = Regex.Match(t.Urls ?? string.Empty, idPattern);
                if (!m.Success)
                    continue;

                var counts = lookup(m.Groups[1].Value);
                if (counts == null)
                {
                    missing.Add(m.Groups[1].Value);
                    continue;
                }

                t.Apply?.Invoke(counts.Value.sid, counts.Value.pir);

                if (!string.IsNullOrEmpty(t.Key))
                    verified.Add(t.Key);
            }

            return missing;
        }

        static async Task ApplyNNMClubAsync(IReadOnlyList<SeedTarget> targets, string title, HashSet<string> verified)
        {
            if (!Any(targets, "nnmclub"))
                return;

            var fresh = await Infrastructure.Trackers.NNMClub.NNMClubSearchSeeders.FetchAsync(title);

            // Пустая выдача поиска означает «трекер не ответил», а не «раздач
            // нет». Спрашивать после этого про пропажи нельзя — под нож пошли
            // бы все раздачи разом.
            if (fresh == null || fresh.Count == 0)
                return;

            var missing = Fill(targets, "nnmclub", @"viewtopic\.php\?t=(\d+)",
                id => fresh.TryGetValue(id, out var c) && c != null ? (c.Sid, c.Pir) : null,
                verified);

            Infrastructure.Trackers.DeletedReleaseProbe.QueueInBackground(
                "nnmclub", missing, Infrastructure.Trackers.NNMClub.NNMClubSearchSeeders.IsDeletedAsync);
        }

        async Task ApplyKinozalAsync(IReadOnlyList<SeedTarget> targets, HashSet<string> verified, params string[] titles)
        {
            if (_kinozal == null || !Any(targets, "kinozal"))
                return;

            var fresh = await _kinozal.LiveSeedersAsync(titles);
            if (fresh.Count == 0)
                return;

            var missing = Fill(targets, "kinozal", @"details\.php\?id=(\d+)",
                id => fresh.TryGetValue(id, out var c) ? c : null, verified);

            Infrastructure.Trackers.DeletedReleaseProbe.QueueInBackground(
                "kinozal", missing, _kinozal.IsDeletedAsync);
        }

        void ApplyTolokaCached(IReadOnlyList<SeedTarget> targets, string title, HashSet<string> verified)
        {
            if (_toloka == null || !Any(targets, "toloka"))
                return;

            Infrastructure.Trackers.LiveSeedersCache.RefreshInBackground(
                "toloka", title, t => _toloka.LiveSeedersAsync(t));

            var fresh = Infrastructure.Trackers.LiveSeedersCache.Cached("toloka", title);
            if (fresh == null || fresh.Count == 0)
                return;

            // Адрес раздачи toloka короткий — «toloka.to/t698087». Длинную
            // форму принимаем ради записей, заведённых по-старому.
            var missing = Fill(targets, "toloka", @"(?:viewtopic\.php\?t=|toloka\.to/t)(\d+)",
                id => fresh.TryGetValue(id, out var c) ? c : null, verified);

            Infrastructure.Trackers.DeletedReleaseProbe.QueueInBackground(
                "toloka", missing, _toloka.IsDeletedAsync);
        }

        void ApplyBitruCached(IReadOnlyList<SeedTarget> targets, string title, HashSet<string> verified)
        {
            if (_bitru == null || !Any(targets, "bitru"))
                return;

            var ids = new List<int>();
            foreach (var t in targets)
            {
                if (!FromTracker(t, "bitru"))
                    continue;

                var m = Regex.Match(t.Urls ?? string.Empty, @"details\.php\?id=(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int id))
                    ids.Add(id);
            }

            if (ids.Count == 0)
                return;

            Infrastructure.Trackers.LiveSeedersCache.RefreshInBackground(
                "bitru", title, _ => _bitru.LiveSeedersAsync(ids));

            var fresh = Infrastructure.Trackers.LiveSeedersCache.Cached("bitru", title);
            if (fresh == null || fresh.Count == 0)
                return;

            Fill(targets, "bitru", @"details\.php\?id=(\d+)",
                id => fresh.TryGetValue(id, out var c) ? c : null, verified);
        }

        static void ApplyRutrackerCached(IReadOnlyList<SeedTarget> targets, HashSet<string> verified, params string[] titles)
        {
            if (!Any(targets, "rutracker"))
                return;

            // Номера тем нужны обновлению: чего не окажется в выдаче поиска,
            // оно добьёт походом на страницу раздачи — удалённую тему поиск
            // не показывает вовсе.
            var topicIds = new List<string>();
            foreach (var t in targets)
            {
                if (!FromTracker(t, "rutracker"))
                    continue;

                var m = Regex.Match(t.Urls ?? string.Empty, @"viewtopic\.php\?t=(\d+)");
                if (m.Success)
                    topicIds.Add(m.Groups[1].Value);
            }

            if (topicIds.Count == 0)
                return;

            string key = FirstNotEmpty(titles);
            if (string.IsNullOrWhiteSpace(key))
                return;

            var fresh = Infrastructure.Trackers.Rutracker.RutrackerSearchSeeders.Cached(key);
            Infrastructure.Trackers.Rutracker.RutrackerSearchSeeders.RefreshInBackground(titles, topicIds);

            if (fresh == null || fresh.Count == 0)
                return;

            Fill(targets, "rutracker", @"viewtopic\.php\?t=(\d+)",
                id => fresh.TryGetValue(id, out var c) ? c : null, verified);
        }

        /// <summary>
        /// Чем в адресе опознаётся номер раздачи у каждого трекера, умеющего
        /// сказать «такой раздачи нет».
        /// </summary>
        static readonly (string Name, string Pattern)[] DeadIdPatterns =
        {
            ("rutracker", @"viewtopic\.php\?t=(\d+)"),
            ("nnmclub",   @"viewtopic\.php\?t=(\d+)"),
            ("bitru",     @"details\.php\?id=(\d+)"),
            ("kinozal",   @"details\.php\?id=(\d+)"),
            ("toloka",    @"(?:viewtopic\.php\?t=|toloka\.to/t)(\d+)")
        };

        /// <summary>
        /// Есть ли среди адресов записи та, которой на трекере уже нет.
        /// Скачать такую нельзя, а число сидов у неё — прошлогодний снимок.
        /// </summary>
        public static bool IsDead(string tracker, string urls)
        {
            if (string.IsNullOrEmpty(tracker) || string.IsNullOrEmpty(urls))
                return false;

            // Имя трекера здесь обязательно: адрес сам по себе неоднозначен —
            // «viewtopic.php?t=» есть и у rutracker, и у nnmclub, а
            // «details.php?id=» и у bitru, и у kinozal.
            foreach (var (name, pattern) in DeadIdPatterns)
            {
                if (!tracker.Contains(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var m = Regex.Match(urls, pattern);
                if (m.Success && DeadReleases.IsDead(name, m.Groups[1].Value))
                    return true;
            }

            return false;
        }
    }
}
