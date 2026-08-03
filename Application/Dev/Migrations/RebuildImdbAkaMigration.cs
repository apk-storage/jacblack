using JacBlack.Application.Index;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Application.Dev.Migrations
{
    /// <summary>
    /// Наполняет словарь всеми написаниями названия — русским, оригинальным,
    /// украинским, — чтобы поиск умел переводить запрос.
    ///
    /// Зачем понадобилось. Словарь пополняется в момент ЗАПИСИ раздачи, а
    /// у 321 тысячи записей код уже проставлен разовой миграцией, и повторно
    /// они не сохранялись. Вдобавок пара «код → название» раньше бралась от
    /// первого источника: код у «Венома» принёс yts, где оба поля английские,
    /// и русского написания в словаре не оказалось вовсе — проверено
    /// 31.07.2026, ноль записей со словом «Веном».
    ///
    /// Следствие для человека: «Веном» находил 196 раздач и ни одной от yts,
    /// «Venom» — 213, из них 35 от yts. После этой миграции запрос на одном
    /// языке дотягивается до раздач на другом.
    ///
    /// Записи не меняются: миграция только читает их и пополняет словарь,
    /// поэтому dryRun отличается лишь тем, что не сохраняет словарь на диск.
    /// </summary>
    public sealed class RebuildImdbAkaMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "rebuildImdbAka";

        public RebuildImdbAkaMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run() => Execute(dryRun: false);

        public object DryRun() => Execute(dryRun: true);

        object Execute(bool dryRun)
        {
            long scanned = 0, withCode = 0, pairs = 0;
            int before = ImdbIndex.Count;

            foreach (var item in FileDB.masterDb.ToArray())
            {
                var rows = FileDB.OpenRead(item.Key, cache: false);
                if (rows == null)
                    continue;

                foreach (var kv in rows)
                {
                    var t = kv.Value;
                    if (t == null)
                        continue;

                    scanned++;

                    if (string.IsNullOrWhiteSpace(t.imdb) || string.IsNullOrWhiteSpace(t.name))
                        continue;

                    withCode++;

                    // Пара считается полезной, только если названия разные:
                    // «Venom ↔ Venom» переводу не помогает.
                    if (!string.IsNullOrWhiteSpace(t.originalname)
                        && !string.Equals(t.name, t.originalname, System.StringComparison.OrdinalIgnoreCase))
                        pairs++;

                    ImdbIndex.Remember(t.imdb, t.name, t.originalname, t.relased);
                }
            }

            if (!dryRun)
                ImdbIndex.SaveIfDirty(force: true);

            return new
            {
                ok = true,
                dryRun,
                просмотрено = scanned,
                сКодом = withCode,
                разныхНазваний = pairs,
                кодовБыло = before,
                кодовСтало = ImdbIndex.Count
            };
        }
    }
}
