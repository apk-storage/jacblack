using System;
using System.Collections.Generic;

namespace JacBlack.Infrastructure.Utils
{
    /// <summary>
    /// Совпадает ли запрос с названием, начинаясь с границы слова.
    ///
    /// Зачем понадобилось. Ключ базы — это склейка имени и оригинального
    /// имени, приведённая без пробелов и знаков, а поиск идёт подстрокой.
    /// Из-за этого запрос «Веном» находил «Новый Мир с Стивеном Хокингом»:
    /// в склейке «новыймирсстиве|ном|хокингом» подстрока лежит на стыке слов.
    /// Замер 31.07.2026: из 283 раздач по запросу «Веном» 28 были
    /// документалками про Хокинга.
    ///
    /// Требовать совпадения целого слова нельзя: люди ищут «интерстел»
    /// и ждут «Интерстеллар». Поэтому правило мягче — совпадение должно
    /// НАЧИНАТЬСЯ на границе слова, а дальше может обрываться где угодно.
    ///
    /// Проверка идёт по исходному названию, где пробелы ещё есть: в ключе
    /// индекса их уже нет, и восстановить границы оттуда невозможно.
    /// </summary>
    public static class TitleMatch
    {
        /// <summary>
        /// Разбивает название на «хвосты» — по одному от начала каждого слова,
        /// уже без разделителей. Для «Новый Мир с Хокингом» это
        /// «новыймирсхокингом», «мирсхокингом», «схокингом», «хокингом».
        /// </summary>
        static IEnumerable<string> WordTails(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                yield break;

            var words = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (char c in title)
            {
                if (char.IsLetterOrDigit(c))
                {
                    current.Append(char.ToLowerInvariant(c));
                }
                else if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }

            if (current.Length > 0)
                words.Add(current.ToString());

            if (words.Count == 0)
                yield break;

            // Склеиваем с конца: хвост от каждого слова — это слово плюс всё,
            // что после него. Так каждый хвост строится за один шаг.
            string tail = string.Empty;
            var tails = new string[words.Count];

            for (int i = words.Count - 1; i >= 0; i--)
            {
                tail = words[i] + tail;
                tails[i] = tail;
            }

            foreach (string t in tails)
                yield return t;
        }

        /// <summary>
        /// Начинается ли где-нибудь в названии слово, с которого совпадает
        /// запрос. Запрос ожидается уже приведённым — без пробелов, знаков
        /// и в нижнем регистре (см. StringConvert.SearchName).
        /// </summary>
        public static bool StartsAtWordBoundary(string title, string normalizedQuery)
        {
            if (string.IsNullOrEmpty(normalizedQuery))
                return true;

            foreach (string tail in WordTails(title))
            {
                if (tail.StartsWith(normalizedQuery, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Начинается ли название прямо с запроса. Строже, чем совпадение по
        /// любому слову: «From Villainess to Savior» отсюда не пройдёт.
        /// </summary>
        public static bool StartsWithQuery(string title, string normalizedQuery)
        {
            if (string.IsNullOrEmpty(normalizedQuery))
                return true;

            string t = StringConvert.SearchName(title);
            return !string.IsNullOrEmpty(t) && t.StartsWith(normalizedQuery, StringComparison.Ordinal);
        }

        /// <summary>
        /// Подходит ли раздача под запрос.
        ///
        /// Два запроса живут по разным правилам, и это не прихоть.
        ///
        /// Первый — то, что человек напечатал в строку поиска. Ему помогает
        /// мягкое совпадение: «танец» должно находить «Веном: Последний танец».
        ///
        /// Второй — оригинальное название карточки, его присылает Лампа. Здесь
        /// мягкость губительна: у сериала «Извне» оригинальное название «From»,
        /// обычное английское слово, и совпадение по любому слову втягивало в
        /// выдачу всё подряд — «The Most Heretical Last Boss Queen: From
        /// Villainess to Savior» и подобное. Замер 31.07.2026: по одному
        /// русскому названию приходило 114 верных раздач, а вместе с
        /// оригинальным — 1414, почти сплошь чужих. Поэтому название карточки
        /// обязано стоять в НАЧАЛЕ, а не встречаться где-то внутри.
        /// </summary>
        public static bool Matches(string name, string originalname, string query, string altQuery)
        {
            if (string.IsNullOrEmpty(query) && string.IsNullOrEmpty(altQuery))
                return true;

            // Признак поиска по карточке — присланное оригинальное название.
            // Его добавляет Лампа, и в этом случае строгими должны быть ОБА
            // названия: карточка «Мир» иначе нахватала бы «Дивный новый мир»
            // ровно так же, как «From» нахватал чужие аниме.
            //
            // Строка поиска, набранная руками, оригинального названия не несёт
            // (его подставляет только словарь соответствий, и лишь для полных
            // названий), поэтому там мягкость сохраняется — «танец» по-прежнему
            // находит «Веном: Последний танец».
            if (!string.IsNullOrEmpty(altQuery))
                return Strict(name, originalname, query) || Strict(name, originalname, altQuery);

            return Loose(name, originalname, query);
        }

        static bool Loose(string name, string originalname, string q)
        {
            if (string.IsNullOrEmpty(q))
                return false;

            return StartsAtWordBoundary(name, q) || StartsAtWordBoundary(originalname, q);
        }

        static bool Strict(string name, string originalname, string q)
        {
            if (string.IsNullOrEmpty(q))
                return false;

            return StartsWithQuery(name, q) || StartsWithQuery(originalname, q);
        }
    }
}
