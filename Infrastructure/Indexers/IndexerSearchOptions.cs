using JacBlack.Models.AppConf;

namespace JacBlack.Infrastructure.Indexers
{
    public static class IndexerSearchOptions
    {
        public static SearchSettings Resolve() =>
            AppInit.conf.search ?? new SearchSettings();
    }
}
