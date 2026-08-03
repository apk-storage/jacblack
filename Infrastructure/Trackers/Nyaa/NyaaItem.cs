using System;

namespace JacBlack.Infrastructure.Trackers.Nyaa
{
    /// <summary>
    /// Запись ленты nyaa.si.
    ///
    /// Заведено 29.07.2026 вместо AnimeTosho: тот замер, что его лента стоит
    /// с 8 мая, подтвердился на самом сайте — на главной единственная дата
    /// 08/05/2026. AnimeTosho был витриной над nyaa, поэтому берём источник
    /// напрямую: он жив, отдаёт свежее и, в отличие от сайта AnimeTosho,
    /// кладёт хеш прямо в ленту — magnet собирается без единого лишнего запроса.
    /// </summary>
    public class NyaaItem
    {
        public string Title { get; set; }

        /// <summary>Ссылка на страницу раздачи (guid), она же адрес записи в базе.</summary>
        public string ViewUrl { get; set; }

        /// <summary>Хеш из &lt;nyaa:infoHash&gt; — из него собирается magnet.</summary>
        public string InfoHash { get; set; }

        public DateTime PubDate { get; set; }

        public int Seeders { get; set; }

        public int Leechers { get; set; }

        /// <summary>Размер как его пишет лента: «689.5 MiB».</summary>
        public string SizeName { get; set; }

        /// <summary>Код раздела: 1_2 — аниме с английскими субтитрами, 1_3 — без перевода и так далее.</summary>
        public string CategoryId { get; set; }

        public string CategoryName { get; set; }
    }
}
