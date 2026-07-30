using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JacRed.Application.Index;
using JacRed.Infrastructure.Persistence;
using JacRed.Infrastructure.Networking;

namespace JacRed.Application.Dev.Migrations
{
    /// <summary>
    /// Удаляет файлы шардов, которых нет в индексе masterDb.
    ///
    /// Откуда берутся. База шардируется по имени раздачи. Когда запись меняет
    /// имя (починили разбор, слили дубли, перенесли ключ), она уезжает в другой
    /// файл, а прежний остаётся лежать — из индекса ключ ушёл, файл нет.
    ///
    /// Чем мешают. Помимо места они ВРУТ В ЗАМЕРАХ: любая попытка посчитать
    /// что-то обходом каталога Data/fdb захватывает эти файлы и даёт числа,
    /// которых в базе нет. За 30.07.2026 я на это попался дважды.
    ///
    /// Замер 30.07.2026: 20 311 файлов из 299 973, около 13 МБ.
    ///
    /// Осторожность. Удаляем только то, чего нет в индексе, и только если индекс
    /// вообще загрузился — иначе пустой masterDb означал бы «лишнее всё».
    /// Сначала запускать с dryRun=true.
    /// </summary>
    public sealed class RemoveOrphanShardsMigration : DevMigrationBase, IDevMigration
    {
        public string Name => "removeOrphanShards";

        public RemoveOrphanShardsMigration(IFastDbIndex fastDbIndex) : base(fastDbIndex) { }

        public object Run() => Execute(dryRun: false);

        public object DryRun() => Execute(dryRun: true);

        object Execute(bool dryRun)
        {
            var keys = FileDB.masterDb.Keys.ToArray();

            // Пустой индекс — не повод считать все файлы лишними.
            if (keys.Length == 0)
                return new { ok = false, причина = "индекс masterDb пуст, уборка отменена" };

            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in keys)
            {
                string md5 = HashTo.md5(key);
                expected.Add(md5.Substring(0, 2) + "/" + md5.Substring(2));
            }

            string root = Path.Combine("Data", "fdb");
            if (!Directory.Exists(root))
                return new { ok = false, причина = "каталог Data/fdb не найден" };

            long orphans = 0, bytes = 0, removed = 0, failed = 0;

            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                string prefix = Path.GetFileName(dir);

                foreach (string file in Directory.EnumerateFiles(dir))
                {
                    string relative = prefix + "/" + Path.GetFileName(file);
                    if (expected.Contains(relative))
                        continue;

                    orphans++;

                    try
                    {
                        bytes += new FileInfo(file).Length;
                    }
                    catch (Exception ex)
                    {
                        Infrastructure.Logging.JacRedLog.Swallowed(
                            Infrastructure.Logging.JacRedLogCategories.Fdb,
                            $"не прочитался размер осиротевшего шарда {relative}", ex,
                            Microsoft.Extensions.Logging.LogLevel.Debug);
                    }

                    if (dryRun)
                        continue;

                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Infrastructure.Logging.JacRedLog.Swallowed(
                            Infrastructure.Logging.JacRedLogCategories.Fdb,
                            $"не удалился осиротевший шард {relative}", ex);
                    }
                }
            }

            return new
            {
                ok = true,
                dryRun,
                ключейВИндексе = keys.Length,
                осиротевших = orphans,
                мегабайт = Math.Round(bytes / 1048576.0, 1),
                удалено = removed,
                неудалось = failed
            };
        }
    }
}
