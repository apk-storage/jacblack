using System.Collections.Generic;

namespace JacBlack.Infrastructure.Indexers
{
    public class IndexerSearchRequest
    {
        public string Query { get; set; }

        public string Title { get; set; }

        public string TitleOriginal { get; set; }

        public int Year { get; set; }

        public int IsSerial { get; set; } = -1;

        public string Genres { get; set; }

        public List<int> Categories { get; set; }

        public int? Season { get; set; }

        public int? Episode { get; set; }

        public string Tracker { get; set; }

        public List<string> Trackers { get; set; }

        public bool CardMode { get; set; }

        public string ApiKey { get; set; }

        public bool RqNum { get; set; }

        /// <summary>
        /// Латинское имя, подхваченное из первой выдачи, когда карточка пришла
        /// без него.
        ///
        /// У аниме Лампа шлёт русское название и японское иероглифами
        /// («Адский режим» + «ヘルモード…»), а англоязычные трекеры подписывают
        /// раздачи ромадзи («Hell Mode: Yarikomizuki no Gamer…»). Общего в этих
        /// строках нет ни буквы, поэтому nyaa и knaben выпадали из карточки
        /// целиком: замер 19.08.2026 — по «Адскому режиму» 22 раздачи без
        /// единой nyaa, по «Hell Mode» — 109, из них 60 nyaa.
        ///
        /// Спасают собственные данные: у русских аниме-трекеров (aniliberty,
        /// animelayer, bitru) в записи лежит и русское имя, и ромадзи. Берём
        /// ромадзи оттуда и ищем ещё раз — уже им.
        /// </summary>
        public string TitleRomaji { get; set; }

        /// <summary>
        /// Все латинские написания, подхваченные из выдачи.
        ///
        /// Одного мало: у «Могилы светлячков» релизы подписаны и «Hotaru no
        /// Haka», и «Grave of the Fireflies» — это разные строки, не связанные
        /// ни ромадзи, ни переводом. Беря только самое частое, мы теряли вторую
        /// половину: из 22 раздач nyaa в карточку попадало 7.
        /// </summary>
        public List<string> TitleAliases { get; set; }
    }
}
