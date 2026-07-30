using System;
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace JacRed.Infrastructure.Parsing
{
    /// <summary>
    /// Разбор страниц трекеров через дерево документа вместо регулярок по разметке.
    ///
    /// Зачем: регулярка описывает не структуру, а точную последовательность
    /// символов, и держится на совпадениях, которых автор не задумывал. Живой
    /// пример с rutor: размер раздачи брался шаблоном
    /// `&lt;td align="right"&gt;([^&lt;]+)&lt;/td&gt;` и попадал в нужную ячейку только
    /// потому, что в соседней — числе комментариев — лежит &lt;img&gt;, и `[^&lt;]+`
    /// до закрывающего тега не дотягивал. Убрал бы трекер картинку — и в размер
    /// поехало бы число комментариев, молча, без единой ошибки в логе.
    ///
    /// Разбор ЗАГОЛОВКОВ («Название / Original Name (2024)») сюда не относится:
    /// это обычный текст, и регулярка там — правильный инструмент.
    /// </summary>
    public static class Html
    {
        static readonly HtmlParser Parser = new HtmlParser();

        static readonly Regex LineBreaks = new Regex("[\n\r\t]+", RegexOptions.Compiled);

        /// <summary>
        /// Переводы строк и НЕРАЗРЫВНЫЕ пробелы. Обычных пробелов здесь нет —
        /// это не упущение, см. пояснение в Normalize.
        /// </summary>
        static readonly Regex CollapsibleSpaces = new Regex("[\n\r\t ]+", RegexOptions.Compiled);

        /// <summary>Обычные пробельные ряды. Неразрывный пробел сюда НЕ входит.</summary>
        static readonly Regex OrdinarySpaces = new Regex("[\n\r\t ]+", RegexOptions.Compiled);

        /// <summary>
        /// Как приводить пробелы. Двух режимов быть не должно — но они есть,
        /// потому что парсеры делали это по-разному, и разница уже вмёрзла в базу.
        ///
        /// Выглядели обе строки одинаково: `Regex.Replace(res, "[\n\r\t ]+", " ")`.
        /// Только у Rutor последним символом набора стоял невидимый U+00A0, а у
        /// Kinozal — обычный пробел. То есть первый схлопывал неразрывные пробелы
        /// и не трогал обычные, второй — ровно наоборот.
        ///
        /// Свести к одному можно только вместе с миграцией: имена участвуют в
        /// ключе шарда, и смена нормализации увела бы записи в другие файлы.
        /// </summary>
        public enum Whitespace
        {
            /// <summary>Rutor и подобные: переводы строк убрать, неразрывные пробелы схлопнуть.</summary>
            DropBreaksCollapseNoBreak,

            /// <summary>Kinozal и подобные: схлопнуть обычные пробельные ряды, неразрывные оставить.</summary>
            CollapseSpaces
        }

        public static IHtmlDocument Parse(string html) => Parser.ParseDocument(html ?? string.Empty);

        /// <summary>Текст узла, приведённый к тому же виду, что давал прежний разбор.</summary>
        public static string Text(AngleSharp.Dom.IElement element, Whitespace mode = Whitespace.DropBreaksCollapseNoBreak) =>
            element == null ? string.Empty : Normalize(element.TextContent, mode);

        public static string Attr(AngleSharp.Dom.IElement element, string name, Whitespace mode = Whitespace.DropBreaksCollapseNoBreak) =>
            element == null ? string.Empty : Normalize(element.GetAttribute(name), mode);

        /// <summary>
        /// Повторяет прежнюю обработку буквально, включая её странность, и это
        /// сделано намеренно.
        ///
        /// Странность: схлопывались переводы строк и неразрывные пробелы, а
        /// обычные — нет. В исходном коде это было незаметно, потому что в наборе
        /// `[\n\r\t ]` последним символом стоял невидимый U+00A0, неотличимый
        /// на глаз от пробела. Из-за этого двойные пробелы из заголовков трекера
        /// доходили до базы как есть — и лежат там внутри тысяч записей.
        ///
        /// Чинить это здесь нельзя: база шардируется по имени раздачи, и починка
        /// пробелов увела бы записи в другие файлы, оставив прежние сиротами.
        /// Возможна отдельным шагом и вместе с миграцией.
        ///
        /// Неразрывный пробел записан кодом, а не символом, чтобы следующий
        /// читатель не потратил на эту загадку столько же времени.
        /// </summary>
        public static string Normalize(string value, Whitespace mode = Whitespace.DropBreaksCollapseNoBreak)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (mode == Whitespace.CollapseSpaces)
                return OrdinarySpaces.Replace(value, " ").Trim();

            value = LineBreaks.Replace(value, string.Empty);
            value = CollapsibleSpaces.Replace(value, " ");
            return value.Trim();
        }
    }
}
