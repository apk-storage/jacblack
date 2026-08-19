using System.Threading;
using System.Threading.Tasks;
using JacBlack.Application.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacBlack.Controllers.Cron
{
    /// <summary>
    /// Разовый импорт архива nyaa из дампа AnimeTosho.
    ///
    /// Сама nyaa вглубь не пускает — сто страниц с каждого конца, около 45
    /// тысяч раздач. Дамп закрывшегося зеркала даёт 863 940 записей с 2017
    /// года. Импорт порционный и возобновляемый: `limit` ограничивает число
    /// записей за заход, положение запоминается между запусками.
    /// </summary>
    [Route("/cron/nyaa-import/[action]")]
    public class NyaaImportController : BaseController
    {
        readonly NyaaArchiveImportService _import;

        public NyaaImportController(IMemoryCache memoryCache, NyaaArchiveImportService import) : base(memoryCache)
        {
            _import = import;
        }

        /// <summary>
        /// limit — сколько записей разобрать за заход (0 — до конца файла).
        /// probe — опрашивать ли трекеры о живости (по умолчанию да).
        /// </summary>
        public Task<string> Run(int limit = 0, bool probe = true, CancellationToken cancellationToken = default)
        {
            return _import.ImportAsync(limit, probe, cancellationToken);
        }
    }
}
