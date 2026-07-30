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

        public static IHtmlDocument Parse(string html) => Parser.ParseDocument(html ?? string.Empty);

        /// <summary>Текст узла, приведённый к тому же виду, что давал прежний разбор.</summary>
        public static string Text(AngleSharp.Dom.IElement element) =>
            element == null ? string.Empty : Normalize(element.TextContent);

        public static string Attr(AngleSharp.Dom.IElement element, string name) =>
            element == null ? string.Empty : Normalize(element.GetAttribute(name));

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
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = LineBreaks.Replace(value, string.Empty);
            value = CollapsibleSpaces.Replace(value, " ");
            return value.Trim();
        }
    }
}
