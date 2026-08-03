using System;

namespace JacBlack.Infrastructure.Indexers
{
    /// <summary>
    /// Считается ли число раздающих в записи проверенным по её свежести.
    ///
    /// Зачем. Живой опрос берёт не всех: анонс закрытого трекера посторонним
    /// не отвечает, а поиск под входом есть не у каждого — у toloka, bitru и
    /// lostfilm его нет вовсе, и их раздачи оставались помеченными как
    /// непроверенные всегда, хотя число в них могло быть свежее опрошенного.
    ///
    /// Но обход и есть опрос, только другим путём: он читает листинг трекера,
    /// а там колонка сидов живая. Toloka обходится дважды в час, и запись,
    /// обновлённая двадцать минут назад, несёт число ровно оттуда.
    ///
    /// Порог в три часа выбран по шагу обходов: у самых частых он полчаса,
    /// у редких — сутки. Три часа отсекает вторых и принимает первых.
    /// Больше брать нельзя: смысл отметки в том, чтобы человек отличал
    /// сегодняшнее число от прошлогоднего, а 98.8% базы старше года.
    /// </summary>
    public static class SeedersFreshness
    {
        public const int FreshHours = 3;

        /// <summary>
        /// Трекер вообще не сообщает числа раздающих.
        ///
        /// У lostfilm счётчиков на сайте нет, и разбор проставляет единицу
        /// жёстко — то есть «1 раздающий» никогда не был данными. Отмечаем это
        /// признаком, чтобы выдача могла сказать «трекер не сообщает» вместо
        /// выдуманного числа.
        /// </summary>
        public static bool TrackerHidesSeeders(string trackerName) =>
            !string.IsNullOrEmpty(trackerName)
            && trackerName.Contains("lostfilm", StringComparison.OrdinalIgnoreCase);

        public static bool IsFresh(DateTime updateTime)
        {
            if (updateTime == default)
                return false;

            double hours = (DateTime.Now - updateTime).TotalHours;
            return hours >= 0 && hours < FreshHours;
        }
    }
}
