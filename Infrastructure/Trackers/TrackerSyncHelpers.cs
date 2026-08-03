// Tracker sync shared helpers — parse lock and cron guard patterns.
//
// ParseAsync: TrackerParseLock + RunParseAsync
// ParseAllTask: TrackerWorkFlag + RunParseAllTaskAsync
// ParseLatest: TrackerLatestParseLock + RunParseLatestAsync

using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Parsing;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace JacBlack.Infrastructure.Trackers
{
    /// <summary>Per-tracker exclusive parse lock (thread-safe TryStart / End).</summary>
    public sealed class TrackerParseLock
    {
        bool _workParse;
        readonly object _lock = new object();

        public bool TryStart()
        {
            lock (_lock)
            {
                if (_workParse)
                    return false;

                _workParse = true;
                return true;
            }
        }

        public void End()
        {
            lock (_lock)
            {
                _workParse = false;
            }
        }
    }

    /// <summary>Work flag for secondary jobs (ParseAllTask).</summary>
    public sealed class TrackerWorkFlag
    {
        volatile bool _work;

        public bool TryStart() => System.Threading.Interlocked.CompareExchange(ref _work, true, false) == false;

        public void End() => System.Threading.Interlocked.Exchange(ref _work, false);
    }

    /// <summary>Semaphore guard for ParseLatest (one concurrent run per tracker).</summary>
    public sealed class TrackerLatestParseLock
    {
        readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public Task<bool> TryEnterAsync(CancellationToken cancellationToken = default)
            => _semaphore.WaitAsync(0, cancellationToken);

        public void Exit() => _semaphore.Release();
    }

    public static class TrackerSyncHelpers
    {
        public const string DisabledResult = "disabled";
        public const string WorkResult = "work";

        public static bool IsTrackerDisabled(string trackerName)
        {
            return AppInit.conf?.disable_trackers != null
                && AppInit.conf.disable_trackers.Contains(trackerName, StringComparer.OrdinalIgnoreCase);
        }

        public static void LogParseSkipped(string trackerName, string reason)
        {
            JacBlackLog.Debug(JacBlackLogCategories.Trackers, $"{trackerName}: parse skipped ({reason})");
        }

        public static async Task<string> RunParseAsync(
            string trackerName,
            TrackerParseLock parseLock,
            bool checkDisabled,
            Func<Task<string>> action)
        {
            if (checkDisabled && IsTrackerDisabled(trackerName))
            {
                LogParseSkipped(trackerName, DisabledResult);
                return DisabledResult;
            }

            if (!parseLock.TryStart())
            {
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            var startedAt = System.DateTime.UtcNow;
            ParseCounters.Begin(trackerName);

            try
            {
                return await action();
            }
            finally
            {
                parseLock.End();

                // Единая итоговая строка для всех парсеров. Раньше у каждого был
                // свой формат: Bitru писал «saved N», Anidub «parsed=/added=»,
                // Rutor и Toloka не писали ничего — сравнивать было нечем.
                var c = ParseCounters.End(trackerName);
                if (c != null)
                {
                    ParserLog.Write(trackerName,
                        $"ИТОГ | добавлено={c.Added} обновлено={c.Updated} " +
                        $"всего={c.Added + c.Updated} заняло={(System.DateTime.UtcNow - startedAt).TotalSeconds:F1}с");

                    // Пустой проход — это молчаливая поломка, и она дорогая.
                    // 01.08.2026 nnmclub сутки писал «добавлено=0 обновлено=0»
                    // и ни одной ошибки: сайт поменял вёрстку заголовка, а код
                    // принимал страницу только по дословной подписи
                    // «NNM-Club</title>» и отвергал исправные страницы.
                    // Заметил случайно, через сутки.
                    //
                    // Поэтому пустой проход выносим в общий журнал уровнем
                    // «предупреждение»: в файл трекера никто не смотрит, а
                    // сюда смотрят и отсюда это видно снаружи.
                    if (c.Added == 0 && c.Updated == 0)
                    {
                        JacBlackLog.Warning(JacBlackLogCategories.Trackers,
                            $"{trackerName}: обход завершился впустую — ни одной записи. " +
                            "Похоже, разбор страниц сломался (сменилась вёрстка, слетел вход или закрылся доступ)");
                    }
                }
            }
        }

        public static async Task<string> RunParseAllTaskAsync(
            string trackerName,
            TrackerWorkFlag workFlag,
            bool checkDisabled,
            Func<Task> action,
            CancellationToken cancellationToken = default)
        {
            if (checkDisabled && IsTrackerDisabled(trackerName))
            {
                LogParseSkipped(trackerName, DisabledResult);
                return DisabledResult;
            }

            if (!workFlag.TryStart())
            {
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            // Отметка о ходе обхода. Единственная точка, где это можно сделать
            // честно: признак занятости живёт в памяти, и снаружи «идёт восемь
            // часов» от «не запускался» ничем не отличалось.
            Indexers.CrawlProgress.Begin(trackerName);
            string outcome = "ok";

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await action();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = "отменён";
                throw;
            }
            catch (Exception ex)
            {
                // Глубокий обход идёт часами. Раньше сбой на любой странице
                // обрывал его молча, и со стороны это выглядело как «прошёл
                // до конца»: очередь оставалась неразобранной без единого
                // слова в логе.
                outcome = $"прерван: {ex.GetType().Name}";
                ParserLog.Write(trackerName, $"глубокий обход прерван: {ex.GetType().Name}: {ex.Message}");
                JacBlackLog.Swallowed(JacBlackLogCategories.Parser, $"{trackerName}: глубокий обход прерван", ex, LogLevel.Error);
            }
            finally
            {
                Indexers.CrawlProgress.End(trackerName, outcome);
                workFlag.End();
            }

            return "ok";
        }

        public static async Task<string> RunParseLatestAsync(
            string trackerName,
            TrackerLatestParseLock latestLock,
            bool checkDisabled,
            Func<Task<string>> buildLogAsync,
            CancellationToken cancellationToken = default)
        {
            if (checkDisabled && IsTrackerDisabled(trackerName))
            {
                LogParseSkipped(trackerName, DisabledResult);
                return DisabledResult;
            }

            if (!await latestLock.TryEnterAsync(cancellationToken))
            {
                LogParseSkipped(trackerName, WorkResult);
                return WorkResult;
            }

            try
            {
                var logText = await buildLogAsync();
                return string.IsNullOrWhiteSpace(logText) ? "ok" : logText;
            }
            finally
            {
                latestLock.Exit();
            }
        }
    }
}
