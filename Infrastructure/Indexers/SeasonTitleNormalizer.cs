using System.Text.RegularExpressions;

namespace JacBlack.Infrastructure.Indexers
{
    /// <summary>
    /// Приводит запись сезона в заголовке к форме, которую понимает парсер Лампы.
    ///
    /// Лампа раскладывает раздачи сериала по сезонам, разбирая номер из ТЕКСТА
    /// заголовка, а не из тега <c>season</c> в фиде. Уверенно она берёт форму
    /// «N сезон» (число впереди): «Пацаны (3 сезон: 1-8 серии)» попадает под
    /// 3 сезон. А форму «сезон N» (число в хвосте, за скобками с кодеком) —
    /// «Пацаны / The Boys (2022) [AV1/2160p] [DV Profile 10.1] (сезон 3,
    /// серии 1-8)» — не распознаёт, и такая раздача в списке сезона не видна,
    /// хотя JacBlack её отдаёт (проверено: в вебе есть, в Лампе нет).
    ///
    /// Правка минимальна и не портит заголовок: переставляем «сезон N» → «N
    /// сезон» на месте, читается естественно. Трогаем только когда формы
    /// «N сезон» в заголовке ещё нет — иначе Лампа и так разберёт, и вмешиваться
    /// незачем.
    /// </summary>
    public static class SeasonTitleNormalizer
    {
        // Число перед словом «сезон» — форма, которую Лампа уже понимает.
        static readonly Regex Head = new Regex(@"\d{1,2}\s*сезон", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // «сезон N» / «сезон: N» — число в хвосте. Множественное «сезоны»
        // (число впереди, «1-3 сезоны») сюда не попадает: после «сезон» тут
        // сразу разделитель и цифра, а не «ы».
        static readonly Regex Tail = new Regex(@"сезон\s*:?\s*(\d{1,2})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Normalize(string title)
        {
            if (string.IsNullOrEmpty(title))
                return title;

            // Уже есть «N сезон» — Лампа разберёт, не вмешиваемся.
            if (Head.IsMatch(title))
                return title;

            if (!Tail.IsMatch(title))
                return title;

            // Переставляем ТОЛЬКО первое вхождение — этого хватает Лампе, а
            // остальной текст («серии 1-8 из 8») остаётся нетронутым.
            return Tail.Replace(title, m => m.Groups[1].Value + " сезон", 1);
        }
    }
}
