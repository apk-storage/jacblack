using System.Threading;
using JacRed.Application.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers.Cron
{
    [Route("/cron/health/[action]")]
    public class HealthCheckController : BaseController
    {
        readonly ParserHealthService _health;

        public HealthCheckController(IMemoryCache memoryCache, ParserHealthService health) : base(memoryCache)
        {
            _health = health;
        }

        /// <summary>Последний посчитанный отчёт. Дёшево, отдаётся мгновенно.</summary>
        public JsonResult Parsers() => Json(_health.LastReport());

        /// <summary>Пересчитать отчёт. Полный обход базы, поэтому по расписанию.</summary>
        public JsonResult Rebuild(CancellationToken cancellationToken) => Json(_health.Rebuild(cancellationToken));
    }
}
