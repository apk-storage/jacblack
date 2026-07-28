namespace JacRed.Models.AppConf
{
    /// <summary>
    /// Служба, превращающая идентификатор Кинопоиска или IMDb в название.
    /// Нужна, когда поиск приходит видом `tt0111161` или `kp326`: по такому
    /// запросу в базе искать нечего, сперва надо узнать имя.
    ///
    /// В исходниках апстрима адрес и токен стояли прямо в коде, причём токен
    /// чужой — то есть запросы шли из чужой квоты. Вынесено в настройку
    /// 28.07.2026, по умолчанию токена нет и обращений наружу не происходит.
    /// </summary>
    public class TitleApiSettings
    {
        public bool enable { get; set; } = true;

        public string url { get; set; } = "https://api.apbugall.org/";

        /// <summary>Пустой — обращений не будет, поиск по идентификатору просто не найдёт названия.</summary>
        public string token { get; set; } = "";

        public int timeoutSeconds { get; set; } = 8;

        /// <summary>Сколько держать найденное название в памяти, часов.</summary>
        public int cacheHours { get; set; } = 24;
    }
}
