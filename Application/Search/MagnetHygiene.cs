using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using MonoTorrent;

namespace JacRed.Application.Search
{
    /// <summary>
    /// Приводит magnet-ссылки в порядок перед отдачей клиенту: выкидывает заведомо
    /// бесполезные трекеры и дописывает рабочие тем раздачам, у которых их нет.
    /// Делается на выдаче, а не при индексации — правка применяется ко всей базе
    /// мгновенно, а список трекеров можно менять без переиндексации.
    /// </summary>
    public static class MagnetHygiene
    {
        /// <summary>
        /// Возвращает исправленную ссылку. При любой неожиданности отдаёт исходную:
        /// испортить рабочую ссылку хуже, чем оставить её неоптимальной.
        /// </summary>
        public static string Clean(string magnet)
        {
            var conf = AppInit.conf.magnet;
            if (conf == null || !conf.enable || string.IsNullOrWhiteSpace(magnet))
                return magnet;

            try
            {
                var link = MagnetLink.Parse(magnet);
                string hex = link.InfoHashes?.V1OrV2?.ToHex();
                if (string.IsNullOrWhiteSpace(hex))
                    return magnet;

                var announces = Filter(link.AnnounceUrls, conf.stripTrackers);

                if (announces.Count == 0 && conf.addDefaultTrackers && conf.defaultTrackers != null)
                {
                    foreach (string tr in conf.defaultTrackers)
                    {
                        if (!string.IsNullOrWhiteSpace(tr))
                            announces.Add(tr.Trim());
                    }
                }

                // Ничего не изменилось — не трогаем строку вовсе.
                int before = link.AnnounceUrls?.Count ?? 0;
                if (before == announces.Count && before > 0)
                    return magnet;

                return Compose(hex, link.Name, announces);
            }
            catch (FormatException)
            {
                return magnet;
            }
            catch (ArgumentException)
            {
                return magnet;
            }
        }

        /// <summary>
        /// Список трекеров раздачи после чистки и дополнения — то, что нужно опросу
        /// живых сидов. Пустой список означает, что спрашивать некого.
        /// </summary>
        public static List<string> AnnounceUrls(string magnet)
        {
            var conf = AppInit.conf.magnet;
            if (string.IsNullOrWhiteSpace(magnet))
                return new List<string>();

            try
            {
                var link = MagnetLink.Parse(magnet);
                var announces = Filter(link.AnnounceUrls, conf?.stripTrackers);

                if (announces.Count == 0 && conf != null && conf.addDefaultTrackers && conf.defaultTrackers != null)
                    announces.AddRange(conf.defaultTrackers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));

                return announces;
            }
            catch (FormatException)
            {
                return new List<string>();
            }
            catch (ArgumentException)
            {
                return new List<string>();
            }
        }

        static List<string> Filter(IList<string> announces, List<string> strip)
        {
            var result = new List<string>();
            if (announces == null)
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string a in announces)
            {
                if (string.IsNullOrWhiteSpace(a))
                    continue;

                if (strip != null && strip.Any(s => !string.IsNullOrWhiteSpace(s) &&
                                                    a.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                if (seen.Add(a))
                    result.Add(a);
            }

            return result;
        }

        static string Compose(string hex, string name, List<string> announces)
        {
            var sb = new StringBuilder("magnet:?xt=urn:btih:").Append(hex.ToLowerInvariant());

            if (!string.IsNullOrWhiteSpace(name))
                sb.Append("&dn=").Append(HttpUtility.UrlEncode(name));

            foreach (string a in announces)
                sb.Append("&tr=").Append(HttpUtility.UrlEncode(a));

            return sb.ToString();
        }
    }
}
