using System;

namespace JacBlack.Application.Search
{
    /// <summary>
    /// Приводит ссылку на раздачу к живому домену.
    ///
    /// Работает на выдаче, рядом с гигиеной magnet-ссылок: в базе адрес
    /// остаётся тем, каким его записал обход, а клиенту уходит рабочий.
    /// Так изменение применяется ко всей базе сразу, без миграции.
    /// </summary>
    public static class TrackerUrlHygiene
    {
        public static string Canonical(string url)
        {
            var conf = AppInit.conf?.urlhygiene;
            if (conf == null || !conf.enable || conf.replaceHosts == null || conf.replaceHosts.Count == 0)
                return url;

            if (string.IsNullOrWhiteSpace(url))
                return url;

            Uri uri;
            try { uri = new Uri(url); }
            catch (UriFormatException) { return url; }

            foreach (var pair in conf.replaceHosts)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                if (!uri.Host.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Меняем только имя хоста: путь и параметры у трекеров при
                // переезде на зеркало те же, а вот схему трогать нельзя —
                // у части зеркал нет https.
                var builder = new UriBuilder(uri) { Host = pair.Value };

                // UriBuilder добавляет порт по умолчанию явно — убираем,
                // иначе в ссылке появится «:443», которого в базе не было.
                if ((uri.Scheme == "https" && builder.Port == 443) || (uri.Scheme == "http" && builder.Port == 80))
                    builder.Port = -1;

                return builder.Uri.ToString();
            }

            return url;
        }
    }
}
