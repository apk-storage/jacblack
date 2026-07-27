using JacRed.Application.Dev.Migrations;

namespace JacRed.Application.Dev
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

        public DevMigrationService(
            FixKnabenNamesMigration fixKnabenNames,
            FixBitruNamesMigration fixBitruNames,
            CleanupMigrations cleanup,
            FixAnilibertyUrlsMigration fixAnilibertyUrls,
            RemoveDuplicateAnilibertyMigration removeDuplicateAniliberty,
            FixAnimelayerDuplicatesMigration fixAnimelayerDuplicates,
            FixAnimeToshoNamesMigration fixAnimeToshoNames,
            FixAnimeToshoUrlsMigration fixAnimeToshoUrls)
        {
            _fixAnimeToshoNames = fixAnimeToshoNames;
            _fixAnimeToshoUrls = fixAnimeToshoUrls;
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
    }
}
