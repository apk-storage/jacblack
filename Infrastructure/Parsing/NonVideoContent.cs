using System.Text.RegularExpressions;

namespace JacBlack.Infrastructure.Parsing
{
    /// <summary>
    /// Опознаёт раздачи, которые видео не являются вовсе: книги, музыку,
    /// картинки, шаблоны, софт.
    ///
    /// Зачем. У nnmclub раздел раздачи в выдаче назван словами, и книжные
    /// разделы наш разбор не опознаёт — работает правило «неопознанное считаем
    /// кино». В итоге книга получает тип `movie`, проходит отбор по типам и
    /// ложится в базу наравне с фильмами. Замер 15.08.2026: таких записей
    /// 32 435, это 1.65% всей базы и 14% всего nnmclub — курсы по фотошопу,
    /// векторный клипарт, энциклопедии на ISO, обои 4K. Человек ищет фильм,
    /// а получает мастер-класс по валянию иглой.
    ///
    /// Судим по СОДЕРЖИМОМУ, а не по разделу: раздел бывает смешанным, и
    /// отказ от целого раздела выбросил бы вместе с книгами настоящее видео.
    ///
    /// Тонкость со звуком, на которой легко ошибиться. `[MP3]` у фильма — это
    /// звуковая дорожка, а не музыкальный альбом: «Incidente 2008
    /// [DVDRip][xivD][MP3][Spanish].avi». Поэтому звуковые форматы считаем
    /// признаком не-видео только тогда, когда рядом НЕТ признаков видео.
    /// По той же причине не трогаем `[DOC]`: у нас это встречается пометкой
    /// документального фильма.
    /// </summary>
    public static class NonVideoContent
    {
        /// <summary>Форматы, которыми видео не бывает никогда.</summary>
        static readonly Regex NeverVideo = new Regex(
            @"\[(PDF|FB2|EPUB|DJVU|MOBI|AZW3|CBR|CBZ|AEP|PSD|AI,\s*EPS|EPS|CDR|OBJ|FBX|BLEND|APK|EXE|JPE?G|PNG|TIFF)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Звук: признак не-видео только в отсутствие признаков видео.</summary>
        static readonly Regex Audio = new Regex(
            @"\[(MP3|FLAC|APE|WAV|AAC|OGG)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>То, чем описывают видео: качество, источник, кодек, контейнер.</summary>
        static readonly Regex VideoHints = new Regex(
            @"\b(BDRip|BDRemux|WEB-?DL|WEBRip|HDRip|DVDRip|DVD9|DVD5|SATRip|TVRip|CAMRip|1080[pi]|720[pi]|2160[pi]|H\.?26[45]|HEVC|x26[45]|XviD|DivX|AVC|MKV|AVI)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Как nnmclub называет заведомо не-кино. Слова взяты из живой базы,
        /// а не придуманы: «Футажи» и «Обои» приходят с пометкой 1080p и 4K,
        /// то есть по признакам видео их не отличить.
        /// </summary>
        static readonly Regex Words = new Regex(
            @"векторный клипарт|проекты\s*-\s|фотография\s*-\s|стоковые изображени|футаж|обои\s*-\s|шаблоны|пресеты|кисти для|3D модел|звуковые эффект",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Видео ли это. Пустое название сомнению не подлежит — пропускаем.</summary>
        public static bool IsNotVideo(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return false;

            if (NeverVideo.IsMatch(title) || Words.IsMatch(title))
                return true;

            return Audio.IsMatch(title) && !VideoHints.IsMatch(title);
        }
    }
}
