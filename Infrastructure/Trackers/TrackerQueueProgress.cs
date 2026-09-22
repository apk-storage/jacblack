using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JacBlack.Infrastructure.Logging;
using JacBlack.Infrastructure.Parsing;

namespace JacBlack.Infrastructure.Trackers
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
    ///
    /// Здесь же живёт ВТОРОЙ ЗАХОД по страницам, не давшимся с первого раза,
    /// — см. <see cref="RetryFailedAsync"/>.
    /// </summary>
    public sealed class TrackerQueueProgress
    {
        /// <summary>
        /// Сколько ждать перед вторым заходом. Страницы теряются отрезками,
        /// когда отваливается браузер или протухает cookie; повтор впритык
        /// попал бы в ту же яму, поэтому даём службе минуту прийти в себя.
        /// </summary>
        public static TimeSpan RetryPause = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Доля неудач, выше которой второй заход не делается. Когда не вышло
        /// больше половины круга, беда не в отдельных страницах, а во входе —
        /// 12.09.2026 у rutracker было «разобрано 0, не вышло 14 827», и
        /// повтор там лишь удвоил бы время впустую.
        /// </summary>
        public const double RetryFailShare = 0.5;

        /// <summary>
        /// Потолок на число отложенных повторов: столько замыканий держим в
        /// памяти, остальные неудачи уходят в следующий круг. При обычных для
        /// rutracker 2 400 неудачах запас четырёхкратный.
        /// </summary>
        public const int RetryCap = 10000;

        readonly string _trackerName;
        readonly Action _saveQueue;
        readonly int _savePages;
        readonly int _total;

        DateTime _lastSave = DateTime.UtcNow;
        DateTime _lastReport = DateTime.UtcNow;
        readonly DateTime _started = DateTime.UtcNow;

        readonly List<Func<Task<bool>>> _retries = new List<Func<Task<bool>>>();

        int _done;
        int _failed;
        int _recovered;

        public TrackerQueueProgress(string trackerName, Action saveQueue, int total, int savePages = 50)
        {
            _trackerName = trackerName;
            _saveQueue = saveQueue;
            _total = total;
            _savePages = savePages < 1 ? 50 : savePages;
        }

        public int Done => _done;
        public int Failed => _failed;

        /// <summary>Сколько страниц подобрал второй заход.</summary>
        public int Recovered => _recovered;

        /// <summary>Сколько страниц отложено на второй заход.</summary>
        public int Pending => _retries.Count;

        /// <summary>
        /// Страница обработана: копим счётчики и время от времени сохраняемся.
        ///
        /// <paramref name="retry"/> — как пройти эту же страницу ещё раз.
        /// Передавать его необязательно: трекер, у которого потерь не бывает
        /// (megapeer разбирает все 77 страниц круга), обходится без повтора.
        /// </summary>
        public void PageDone(bool ok, Func<Task<bool>> retry = null)
        {
            if (ok)
                _done++;
            else
            {
                _failed++;
                if (retry != null && _retries.Count < RetryCap)
                    _retries.Add(retry);
            }

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
                JacBlackLog.Swallowed(JacBlackLogCategories.Parser,
                    $"{_trackerName}: ход глубокого обхода не сохранился", ex);
            }
        }

        /// <summary>
        /// Второй заход по страницам, не давшимся с первого раза.
        ///
        /// Зачем. У rutracker в каждом круге терялось около 2 380 страниц из
        /// 14 827 — каждая шестая. Счётчик неудач рос не ровно, а ПАЧКАМИ: 477
        /// подряд при 500 пройденных страницах (21.09.2026). Так себя ведут не
        /// мёртвые разделы, а отрезки, на которых отвалился браузер или
        /// протухла cookie. До 22.09.2026 такие страницы просто ждали
        /// следующего круга — и там падали снова, но уже в другом месте, так
        /// что шестая часть потенциала базы не добиралась никогда.
        ///
        /// Вызывать ПЕРЕД <see cref="Finish"/>: итоговая строка тогда покажет
        /// честный итог круга вместе с подобранным.
        /// </summary>
        public async Task RetryFailedAsync()
        {
            if (_retries.Count == 0)
                return;

            if (_total > 0 && (double)_failed / _total > RetryFailShare)
            {
                ParserLog.Write(_trackerName,
                    $"второй заход пропущен: не вышло {_failed} из {_total} — это отвалившийся вход, а не отдельные страницы");
                _retries.Clear();
                return;
            }

            var берём = _retries.ToArray();
            _retries.Clear();

            ParserLog.Write(_trackerName,
                $"второй заход по непрошедшим: {берём.Length} страниц, пауза {RetryPause.TotalSeconds:F0}с");

            if (RetryPause > TimeSpan.Zero)
                await Task.Delay(RetryPause);

            var начали = DateTime.UtcNow;
            int сделано = 0;

            foreach (var повтор in берём)
            {
                bool ok;
                try
                {
                    ok = await повтор();
                }
                catch (Exception ex)
                {
                    // Одна упавшая страница не должна валить весь второй заход:
                    // он идёт последним, и всё подобранное до этого места уже
                    // лежит в базе.
                    JacBlackLog.Swallowed(JacBlackLogCategories.Parser,
                        $"{_trackerName}: второй заход по странице не удался", ex);
                    ok = false;
                }

                сделано++;

                if (ok)
                {
                    _recovered++;
                    _done++;
                    _failed--;
                }

                if (сделано % _savePages == 0)
                    Save();
            }

            ParserLog.Write(_trackerName,
                $"второй заход завершён: подобрано {_recovered} из {берём.Length}, осталось не вышедших {_failed}, заняло {(DateTime.UtcNow - начали).TotalMinutes:F1} мин");
        }

        /// <summary>Итоговое сохранение и строка в лог.</summary>
        public void Finish()
        {
            Save();

            string подобрано = _recovered > 0 ? $" (из них со второго захода {_recovered})" : "";

            ParserLog.Write(_trackerName,
                $"глубокий обход завершён: разобрано {_done}{подобрано}, не вышло {_failed}, всего в очереди {_total}, заняло {(DateTime.UtcNow - _started).TotalMinutes:F1} мин");
        }
    }
}
