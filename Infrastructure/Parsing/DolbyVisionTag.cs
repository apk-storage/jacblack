using System.Text.RegularExpressions;

namespace JacBlack.Infrastructure.Parsing
{
    /// <summary>Какой Dolby Vision у раздачи. Два значения — столько же различает Лампа.</summary>
    public enum DolbyVisionKind
    {
        None = 0,

        /// <summary>Dolby Vision в любом виде: «Dolby Vision», «DV Profile 10.1», «DV 8.1», «DoVi».</summary>
        Dv,

        /// <summary>Отдельно помеченный источниками «Dolby Vision TV».</summary>
        DvTv
    }

    /// <summary>
    /// Опознаёт Dolby Vision по названию раздачи.
    ///
    /// Зачем отдельно от videotype. Сейчас весь видеотип сводится к «sdr» или
    /// «hdr», и DV-раздача неотличима от обычной. Разница не косметическая:
    /// профиль решает, покажет ли устройство верные цвета. Восьмой профиль
    /// совместим с HDR10 и играет везде, а чистый DV на неподдерживающем
    /// экране даёт вылинявшую картинку.
    ///
    /// Почему именно два значения, а не профиль числом: столько различает
    /// Лампа, ради которой всё и делается. Источники пишут вразнобой —
    /// «Dolby Vision P8», «Dolby Vision Profile 8», «DV 8.1», «DV Profile 10.1»,
    /// «Dolby Vision TV», — и в два ведра это раскладывается однозначно:
    /// помечено TV — значит DV TV, всё остальное — DV.
    ///
    /// Голое «DV» без профиля намеренно НЕ считается признаком: на трекерах это
    /// сокращение встречается в перечислениях озвучек («| D, P, DV |»), и по
    /// нему легко приписать Dolby Vision раздаче, где его нет. Требуем либо
    /// полное написание, либо «DV» с профилем или пометкой TV.
    /// </summary>
    public static class DolbyVisionTag
    {
        static readonly Regex Tv = new Regex(
            @"(dolby\s*vision|dovi|\bdv)\s*[-]?\s*tv\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static readonly Regex Any = new Regex(
            @"dolby\s*vision|dovi|\bdv\s*(profile\s*)?\d",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static DolbyVisionKind Detect(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return DolbyVisionKind.None;

            if (Tv.IsMatch(title))
                return DolbyVisionKind.DvTv;

            return Any.IsMatch(title) ? DolbyVisionKind.Dv : DolbyVisionKind.None;
        }

        /// <summary>Значение для выдачи: «dv», «dvtv» или null.</summary>
        public static string Value(string title) => Detect(title) switch
        {
            DolbyVisionKind.DvTv => "dvtv",
            DolbyVisionKind.Dv => "dv",
            _ => null
        };

        /// <summary>Как пометка выглядит в названии.</summary>
        public static string Label(DolbyVisionKind kind) => kind switch
        {
            DolbyVisionKind.DvTv => "Dolby Vision TV",
            DolbyVisionKind.Dv => "Dolby Vision",
            _ => null
        };

        /// <summary>
        /// Дописывает полное написание «Dolby Vision» тем, кто подписан сокращённо.
        ///
        /// Лампа ищет в названии буквальные слова — в её разборе стоит
        /// `check('dolby vision')` и `check('dolby vision tv')`. Поэтому раздача,
        /// подписанная «DV Profile 10.1» или «DV 8.1», не попадает НИ В ОДИН из
        /// двух её фильтров: слов там нет. Замер 07.08.2026 по «Пацанам» 3 сезона:
        /// в Лампе видно две DV-раздачи из трёх, пропадала как раз nnmclub на
        /// 13.30 ГБ с «DV Profile 10.1».
        ///
        /// Сокращение при этом не трогаем — оно несёт профиль, который человеку
        /// полезен. Просто дописываем слова, которые ищет клиент.
        ///
        /// Чего этим НЕ исправить: «Dolby Vision TV» содержит внутри себя
        /// «dolby vision», поэтому фильтр «Dolby Vision» забирает и TV-раздачи.
        /// Это устройство самой Лампы, подстрокой, и со стороны сервера не
        /// лечится: убрать слова — сломается её же фильтр «Dolby Vision TV».
        /// </summary>
        public static string Normalize(string title)
        {
            var kind = Detect(title);
            if (kind == DolbyVisionKind.None || string.IsNullOrWhiteSpace(title))
                return title;

            string needed = Label(kind);

            // Уже написано словами — дописывать нечего.
            if (title.IndexOf(needed, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return title;

            return title + " | " + needed;
        }

        /// <summary>
        /// Сохраняет упоминание Dolby Vision при склейке копий одной раздачи.
        ///
        /// Склейка идёт по инфохешу — то есть объединяются копии ОДНОГО файла
        /// на разных трекерах, — но название выживает одно, кинозаловское.
        /// А описывают трекеры по-разному: у раздачи 3 сезона «The Boys»
        /// (хеш 59165c9b…) rutor пишет «Dolby Vision Profile 8», а кинозал —
        /// только «HDR, HDR10+». Упоминание терялось, и в Лампе DV-раздача
        /// выглядела обычной: видно было одну DV вместо трёх.
        ///
        /// Возвращает название, к которому при необходимости дописана пометка.
        /// </summary>
        public static string Preserve(string keptTitle, string mergedTitle)
        {
            if (string.IsNullOrWhiteSpace(keptTitle))
                return keptTitle;

            var kept = Detect(keptTitle);
            var merged = Detect(mergedTitle);

            // У выжившего названия признак уже есть, либо у поглощённого его
            // нет — дописывать нечего.
            if (kept != DolbyVisionKind.None || merged == DolbyVisionKind.None)
                return keptTitle;

            return keptTitle + " | " + Label(merged);
        }
    }
}
