namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Прохождение проверки Cloudflare через FlareSolverr — безголовый браузер,
    /// который стоит рядом в compose и не публикуется наружу.
    ///
    /// Понадобился 27.07.2026, когда rutracker (40% базы) ушёл под защиту:
    /// ответ 403 с заголовком `cf-mitigated: challenge`.
    ///
    /// Дешёвый путь «забрать cookie и ходить дальше обычным клиентом» проверен
    /// и не работает: с той же cookie и тем же User-Agent прилетает 403 —
    /// Cloudflare сверяет ещё и отпечаток TLS. Поэтому такие хосты обслуживает
    /// браузер целиком, в одной постоянной сессии.
    /// </summary>
    public class FlareSolverrSettings
    {
        public bool enable { get; set; } = true;

        /// <summary>Адрес службы.</summary>
        public string url { get; set; } = "http://flaresolverr:8191/v1";

        /// <summary>
        /// Сколько ждать ответа браузера, мс. Первое обращение долгое — там
        /// решается задача: на rutracker замерено около 80 секунд. Со 120 000
        /// оно не укладывалось под нагрузкой, поэтому 180 000.
        /// </summary>
        public int maxTimeoutMs { get; set; } = 180000;

        /// <summary>
        /// Через сколько минут простоя закрывать сессию браузера.
        /// Она держит около 700 МБ, а на машине их всего 3,8 ГБ и рядом живёт VPN.
        /// </summary>
        public int sessionIdleMinutes { get; set; } = 10;

        /// <summary>
        /// Сколько часов помнить, что хост закрыт проверкой, и ходить туда
        /// сразу браузером, не тратя запрос на заведомый отказ.
        /// </summary>
        public int guardedHours { get; set; } = 6;
    }
}
