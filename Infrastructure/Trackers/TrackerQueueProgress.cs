using System;
using JacRed.Infrastructure.Logging;
using JacRed.Infrastructure.Parsing;

namespace JacRed.Infrastructure.Trackers
{
    /// <summary>
    /// Сохраняет ход глубокого обхода и пишет о нём в лог.
    ///
    /// Раньше отметка «страница разобрана» жила только в памяти: файл очереди
    /// записывался при её построении и больше никогда. Для обхода на несколько
    /// часов это означало, что любой перезапуск контейнера отбрасывает работу
    /// в самое начало — и заметить это было нечем, потому что в логе о ходе
    /// обхода не было ни строки.
    ///
    /// Сохраняем не на каждой странице: очередь rutracker это 14 827 записей,
    /// переписывать такой файл тысячи раз подряд незачем.
    /// </summary>
    public sealed class TrackerQueueProgress
    {
        readonly string _trackerName;
        readonly Action _saveQueue;
        readonly int _savePages;
        readonly int _total;

        DateTime _lastSave = DateTime.UtcNow;
        DateTime _lastReport = DateTime.UtcNow;
        readonly DateTime _started = DateTime.UtcNow;

        int _done;
        int _failed;

        public TrackerQueueProgress(string trackerName, Action saveQueue, int total, int savePages = 50)
        {
            _trackerName = trackerName;
            _saveQueue = saveQueue;
            _total = total;
            _savePages = savePages < 1 ? 50 : savePages;
        }

        public int Done => _done;
        public int Failed => _failed;

        /// <summary>Страница обработана: копим счётчики и время от времени сохраняемся.</summary>
        public void PageDone(bool ok)
        {
            if (ok)
                _done++;
            else
                _failed++;

            int handled = _done + _failed;

            if (handled % _savePages == 0 || DateTime.UtcNow > _lastSave.AddMinutes(2))
                Save();

            // Раз в пять минут говорим, где мы: обход идёт часами,
            // и молчание неотличимо от зависания.
            if (DateTime.UtcNow > _lastReport.AddMinutes(5))
            {
                _lastReport = DateTime.UtcNow;
                ParserLog.Write(_trackerName,
                    $"глубокий обход: {handled} из {_total} страниц, разобрано {_done}, не вышло {_failed}, идёт {(DateTime.UtcNow - _started).TotalMinutes:F0} мин");
            }
        }

        public void Save()
        {
            try
            {
                _saveQueue();
                _lastSave = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                // Не прерываем обход: страницы разбираются, просто прогресс
                // не лёг на диск. Но знать об этом надо — иначе следующий
                // запуск молча начнёт всё заново.
                JacRedLog.Swallowed(JacRedLogCategories.Parser,
                    $"{_trackerName}: ход глубокого обхода не сохранился", ex);
            }
        }

        /// <summary>Итоговое сохранение и строка в лог.</summary>
        public void Finish()
        {
            Save();

            ParserLog.Write(_trackerName,
                $"глубокий обход завершён: разобрано {_done}, не вышло {_failed}, всего в очереди {_total}, заняло {(DateTime.UtcNow - _started).TotalMinutes:F1} мин");
        }
    }
}
