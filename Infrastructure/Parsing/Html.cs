using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace JacBlack.Infrastructure.Parsing
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

        static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        public static IHtmlDocument Parse(string html) => Parser.ParseDocument(html ?? string.Empty);

        public static string Text(AngleSharp.Dom.IElement element) =>
            element == null ? string.Empty : Normalize(element.TextContent);

        public static string Attr(AngleSharp.Dom.IElement element, string name) =>
            element == null ? string.Empty : Normalize(element.GetAttribute(name));

        /// <summary>
        /// Любой пробельный ряд — в один обычный пробел, края обрезаются.
        ///
        /// Здесь была история, которую стоит помнить. Парсеры делали это ТРЕМЯ
        /// разными способами, а строки кода выглядели одинаково:
        /// `Regex.Replace(res, "[\n\r\t ]+", " ")`. У rutor последним символом
        /// набора стоял невидимый U+00A0, у kinozal — обычный пробел, у selezen
        /// вместо набора было `\s+`. То есть первый схлопывал неразрывные
        /// пробелы и не трогал обычные, второй ровно наоборот, третий всё подряд.
        /// Отличить их на глаз было нельзя.
        ///
        /// Разница вмёрзла в базу: двойные пробелы в именах у rutor, неразрывные
        /// у animelayer. Замер 30.07.2026 показал, что задето 0.14% записей —
        /// достаточно мало, чтобы свести к одному правилу и перенести ключи
        /// миграцией NormalizeWhitespace, а не тащить три режима дальше.
        /// </summary>
        public static string Normalize(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : Whitespace.Replace(value, " ").Trim();
    }
}
