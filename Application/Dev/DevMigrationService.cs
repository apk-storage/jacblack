using JacBlack.Application.Dev.Migrations;

namespace JacBlack.Application.Dev
{
    public class DevMigrationService : IDevMigrationService
    {
        readonly FixKnabenNamesMigration _fixKnabenNames;
        readonly FixBitruNamesMigration _fixBitruNames;
        readonly CleanupMigrations _cleanup;
        readonly FixAnilibertyUrlsMigration _fixAnilibertyUrls;
        readonly RemoveDuplicateAnilibertyMigration _removeDuplicateAniliberty;
        readonly FixAnimelayerDuplicatesMigration _fixAnimelayerDuplicates;
        readonly FixAnimeToshoNamesMigration _fixAnimeToshoNames;
        readonly FixAnimeToshoUrlsMigration _fixAnimeToshoUrls;
        readonly FixDomainDuplicatesMigration _fixDomainDuplicates;
        readonly RemoveNonTmdbContentMigration _removeNonTmdb;
        readonly NormalizeWhitespaceMigration _normalizeWhitespace;
        readonly RemoveOrphanShardsMigration _removeOrphanShards;
        readonly FillImdbFromDictionaryMigration _fillImdb;
        readonly FillKinopoiskFromDictionaryMigration _fillKinopoisk;
        readonly FixMissingYearMigration _fixMissingYear;
        readonly RebuildImdbAkaMigration _rebuildImdbAka;

        public DevMigrationService(
            FixKnabenNamesMigration fixKnabenNames,
            FixBitruNamesMigration fixBitruNames,
            CleanupMigrations cleanup,
            FixAnilibertyUrlsMigration fixAnilibertyUrls,
            RemoveDuplicateAnilibertyMigration removeDuplicateAniliberty,
            FixAnimelayerDuplicatesMigration fixAnimelayerDuplicates,
            FixAnimeToshoNamesMigration fixAnimeToshoNames,
            FixAnimeToshoUrlsMigration fixAnimeToshoUrls,
            FixDomainDuplicatesMigration fixDomainDuplicates,
            RemoveNonTmdbContentMigration removeNonTmdb,
            NormalizeWhitespaceMigration normalizeWhitespace,
            RemoveOrphanShardsMigration removeOrphanShards,
            FillImdbFromDictionaryMigration fillImdb,
            FillKinopoiskFromDictionaryMigration fillKinopoisk,
            FixMissingYearMigration fixMissingYear,
            RebuildImdbAkaMigration rebuildImdbAka)
        {
            _fixMissingYear = fixMissingYear;
            _fixAnimeToshoNames = fixAnimeToshoNames;
            _fixAnimeToshoUrls = fixAnimeToshoUrls;
            _fixDomainDuplicates = fixDomainDuplicates;
            _removeNonTmdb = removeNonTmdb;
            _normalizeWhitespace = normalizeWhitespace;
            _removeOrphanShards = removeOrphanShards;
            _fillImdb = fillImdb;
            _fillKinopoisk = fillKinopoisk;
            _rebuildImdbAka = rebuildImdbAka;
            _fixKnabenNames = fixKnabenNames;
            _fixBitruNames = fixBitruNames;
            _cleanup = cleanup;
            _fixAnilibertyUrls = fixAnilibertyUrls;
            _removeDuplicateAniliberty = removeDuplicateAniliberty;
            _fixAnimelayerDuplicates = fixAnimelayerDuplicates;
        }

        public object FixKnabenNames() => _fixKnabenNames.Run();

        public object FixBitruNames() => _fixBitruNames.Run();

        public object RemoveNullValues() => _cleanup.RemoveNullValues();

        public object RemoveBucket(string key, string migrateName = null, string migrateOriginalname = null) =>
            _cleanup.RemoveBucket(key, migrateName, migrateOriginalname);

        public object FixEmptySearchFields() => _cleanup.FixEmptySearchFields();

        public object MigrateAnilibertyUrls() => _fixAnilibertyUrls.Run();

        public object RemoveDuplicateAniliberty() => _removeDuplicateAniliberty.Run();

        public object FixAnimelayerDuplicates() => _fixAnimelayerDuplicates.Run();

        public object FixAnimeToshoNames() => _fixAnimeToshoNames.Run();

        public object FixAnimeToshoUrls() => _fixAnimeToshoUrls.Run();

        /// <summary>Слияние записей, задвоившихся при смене домена трекера. dryRun считает, не трогая базу.</summary>
        /// <summary>Чистка того, чего нет в TMDB. dryRun считает, не трогая базу.</summary>
        public object RemoveNonTmdbContent(bool dryRun) =>
            dryRun ? _removeNonTmdb.DryRun() : _removeNonTmdb.Run();

        /// <summary>Единая нормализация пробелов в именах с переносом ключей.</summary>
        public object NormalizeWhitespace(bool dryRun) =>
            dryRun ? _normalizeWhitespace.DryRun() : _normalizeWhitespace.Run();

        /// <summary>Уборка файлов шардов, которых нет в индексе.</summary>
        public object RemoveOrphanShards(bool dryRun) =>
            dryRun ? _removeOrphanShards.DryRun() : _removeOrphanShards.Run();

        /// <summary>Проставить код IMDB по названию и году из словаря.</summary>
        public object FillImdbFromDictionary(bool dryRun) =>
            dryRun ? _fillImdb.DryRun() : _fillImdb.Run();

        /// <summary>Проставить код Кинопоиска по названию и году из словаря.</summary>
        public object FillKinopoiskFromDictionary(bool dryRun) =>
            dryRun ? _fillKinopoisk.DryRun() : _fillKinopoisk.Run();

        /// <summary>Восстановить год из заголовка там, где разбор его потерял.</summary>
        public object FixMissingYear(bool dryRun) =>
            dryRun ? _fixMissingYear.DryRun() : _fixMissingYear.Run();

        /// <summary>Наполнить словарь всеми написаниями названия для перевода запроса.</summary>
        public object RebuildImdbAka(bool dryRun) =>
            dryRun ? _rebuildImdbAka.DryRun() : _rebuildImdbAka.Run();

        public object FixDomainDuplicates(bool dryRun) =>
            dryRun ? _fixDomainDuplicates.DryRun() : _fixDomainDuplicates.Run();
    }
}
