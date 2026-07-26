namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Фоновая проверка живости раздач. Отдельно от поискового опроса:
    /// тот работает по выдаче и торопится, этот идёт по всей базе и никуда не спешит.
    /// </summary>
    public class SweepSettings
    {
        public bool enable { get; set; } = true;

        /// <summary>Сколько ключей базы разбирать за один прогон.</summary>
        public int batchKeys { get; set; } = 3000;

        /// <summary>Потолок по времени на прогон, секунд. Дошли до него — останавливаемся и запоминаем место.</summary>
        public int maxSeconds { get; set; } = 900;

        /// <summary>Пауза между обращениями к одному трекеру, мс. Не долбим чужие серверы.</summary>
        public int delayMs { get; set; } = 400;

        /// <summary>Таймаут одного обращения к трекеру, мс. Здесь можно щедрее, чем в поиске.</summary>
        public int trackerTimeoutMs { get; set; } = 3000;

        /// <summary>Хешей в одном пакете. Протокол допускает до 74.</summary>
        public int maxHashesPerRequest { get; set; } = 70;

        /// <summary>
        /// Сколько нулей подряд считать приговором. Достигшие порога попадают
        /// в отчёт, а удаляются только если включено удаление.
        /// </summary>
        public int deadThreshold { get; set; } = 5;

        /// <summary>
        /// Удалять достигших порога. Выключено намеренно: ноль на трекере
        /// не доказывает смерть — раздача может жить на DHT или на трекере,
        /// которого нет в ссылке. Сначала копим статистику, потом решаем.
        /// </summary>
        public bool deleteDead { get; set; } = false;
    }
}
