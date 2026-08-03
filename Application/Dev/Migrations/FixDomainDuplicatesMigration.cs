using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using JacBlack.Application.Index;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Application.Dev.Migrations
{
    /// <summary>
    /// Схлопывает записи, задвоившиеся из-за смены домена трекера.
    ///
    /// Адрес раздачи — ключ записи, а домены меняются. Одна и та же тема
    /// rutracker лежит дважды: под `rutracker.net` и под `rutracker.org`,
    /// номер темы у обеих один. Замер 29.07.2026: у rutracker **половина**
    /// ссылок в базе на `.net`, у kinozal 77% на переставший резолвиться
    /// `kinozal.tv`.
    ///
    /// На выдаче повторы уже схлопываются (DuplicateFilter), но в базе лежат
    /// обе записи: занимают место и замедляют чтение шардов.
    ///
    /// Сначала запускать с `dryRun=true` — посчитает, ничего не трогая.
    /// </summary>
    public sealed class FixDomainDuplicatesMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "fixDomainDuplicates";

        public FixDomainDuplicatesMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        /// <summary>
        /// Какой домен считать основным у каждого трекера. Остальные его
        /// адреса — двойники, которые сливаются в основной.
        /// </summary>
        static readonly Dictionary<string, string> CanonicalHost = new(StringComparer.OrdinalIgnoreCase)
        {
            ["rutracker"] = "rutracker.org",
            ["kinozal"] = "kinozal.guru"
        };

        static readonly Regex RxHash = new Regex("btih:([0-9a-fA-F]{40})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public object Run() => Execute(dryRun: false);

        /// <summary>Считает, что было бы сделано, ничего не меняя.</summary>
        public object DryRun() => Execute(dryRun: true);

        object Execute(bool dryRun)
        {
            int scanned = 0, merged = 0, keysTouched = 0;
            var byTracker = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var shard = dryRun ? null : FileDB.OpenWrite(item.Key);

                try
                {
                    var rows = dryRun
                        ? FileDB.OpenRead(item.Key, cache: false)?.ToList()
                        : shard?.Database.ToList();

                    if (rows == null || rows.Count == 0)
                        continue;

                    // Группируем по «трекер + хеш раздачи»: одна раздача,
                    // разные адреса.
                    var groups = new Dictionary<string, List<KeyValuePair<string, Models.Details.TorrentDetails>>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kv in rows)
                    {
                        var t = kv.Value;
                        if (t == null || string.IsNullOrEmpty(t.trackerName))
                            continue;

                        // Раньше здесь стоял отбор только по трекерам со сменой
                        // домена. Сплошная проверка 29.07.2026 показала, что
                        // повторы есть и у остальных: megapeer 325 и lostfilm 235
                        // на выборке в 71 тысячу записей. Признак «тот же трекер,
                        // тот же хеш» работает для всех, а какой из адресов
                        // основной — решает CanonicalHost, если он для трекера задан.

                        var m = RxHash.Match(t.magnet ?? "");
                        if (!m.Success)
                            continue;

                        scanned++;
                        string key = t.trackerName + ":" + m.Groups[1].Value.ToLowerInvariant();

                        if (!groups.TryGetValue(key, out var list))
                            groups[key] = list = new List<KeyValuePair<string, Models.Details.TorrentDetails>>();

                        list.Add(kv);
                    }

                    var toRemove = new List<string>();

                    foreach (var pair in groups)
                    {
                        if (pair.Value.Count < 2)
                            continue;

                        string tracker = pair.Value[0].Value.trackerName;

                        // Оставляем запись на основном домене, если он для этого
                        // трекера задан. Иначе — самую живую: у остальных трекеров
                        // двойники берутся не от смены домена, и «правильного»
                        // адреса среди них нет, есть только более свежий.
                        var keep = default(KeyValuePair<string, Models.Details.TorrentDetails>);

                        if (CanonicalHost.TryGetValue(tracker, out string canonical))
                            keep = pair.Value.FirstOrDefault(kv => HostOf(kv.Key) == canonical);

                        if (keep.Key == null)
                            keep = pair.Value.OrderByDescending(kv => kv.Value.sid).ThenByDescending(kv => kv.Value.updateTime).First();

                        foreach (var kv in pair.Value)
                        {
                            if (ReferenceEquals(kv.Value, keep.Value))
                                continue;

                            // Из двойника забираем лучшее, что в нём было:
                            // сиды могли обновиться именно у него.
                            if (kv.Value.sid > keep.Value.sid)
                            {
                                keep.Value.sid = kv.Value.sid;
                                keep.Value.pir = kv.Value.pir;
                            }

                            if (kv.Value.updateTime > keep.Value.updateTime)
                                keep.Value.updateTime = kv.Value.updateTime;

                            toRemove.Add(kv.Key);
                            merged++;
                            byTracker[tracker] = byTracker.GetValueOrDefault(tracker) + 1;
                        }
                    }

                    if (toRemove.Count == 0 || dryRun)
                        continue;

                    foreach (string url in toRemove)
                        shard.Database.Remove(url);

                    keysTouched++;
                    shard.savechanges = true;

                    if (shard.Database.Count == 0)
                        FileDB.RemoveKeyFromMasterDb(item.Key);
                }
                finally
                {
                    shard?.Dispose();
                }
            }

            if (!dryRun)
            {
                FileDB.SaveChangesToFile();
                TryRebuildFastDb();
            }

            return new
            {
                ok = true,
                dryRun,
                просмотрено = scanned,
                слитоБы = dryRun ? merged : 0,
                слито = dryRun ? 0 : merged,
                шардовЗатронуто = keysTouched,
                поТрекерам = byTracker
            };
        }

        static string HostOf(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try { return new Uri(url).Host; }
            catch (UriFormatException) { return null; }
        }
    }
}
