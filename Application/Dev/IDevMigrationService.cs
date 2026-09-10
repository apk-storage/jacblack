namespace JacBlack.Application.Dev
{
    public interface IDevMigrationService
    {
        object FixKnabenNames();
        object FixBitruNames();
        object RemoveNullValues();
        object RemoveBucket(string key, string migrateName = null, string migrateOriginalname = null);
        object FixEmptySearchFields();
        object MigrateAnilibertyUrls();
        object RemoveDuplicateAniliberty();
        object FixAnimelayerDuplicates();
        object FixAnimeToshoNames();
        object FixAnimeToshoUrls();

        object FixDomainDuplicates(bool dryRun);

        object RemoveNonTmdbContent(bool dryRun);

        object NormalizeWhitespace(bool dryRun);

        /// <summary>Схлопывает повторы имён внутри trackerName. dryRun считает, не трогая базу.</summary>
        object FixTrackerNameDuplicates(bool dryRun);

        object RemoveOrphanShards(bool dryRun);

        object FillImdbFromDictionary(bool dryRun);
        object FillKinopoiskFromDictionary(bool dryRun);
        object FixMissingYear(bool dryRun);
        object RebuildImdbAka(bool dryRun);
    }
}
