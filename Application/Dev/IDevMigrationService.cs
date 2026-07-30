namespace JacRed.Application.Dev
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

        object RemoveOrphanShards(bool dryRun);

        object FillImdbFromDictionary(bool dryRun);
    }
}
