using JacBlack.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using JacBlack.Infrastructure.Persistence;

namespace JacBlack.Controllers
{
    public class HealthController : Controller
    {
        [Route("health")]
        public IActionResult Health()
        {
            return Json(new Dictionary<string, string>
            {
                ["status"] = "OK"
            });
        }

        /// <summary>
        /// Версия протокола JacBlack. Клиенты (Lampa, Lampac) проверяют этим адресом,
        /// тот ли перед ними сервер, и ждут голое число в text/plain — ровно так,
        /// как отдаёт оригинальный JacBlack. Отдав вместо этого JSON со сведениями
        /// о сборке, мы для них становимся чужим сервером, и поисковые запросы
        /// до нас просто не доходят. Сведения о сборке — на /version/build.
        /// </summary>
        [Route("version")]
        public IActionResult Version()
        {
            return Content(ProtocolVersion, "text/plain; charset=utf-8");
        }

        const string ProtocolVersion = "11";

        /// <summary>Сведения о сборке. Раньше жили на /version, но тот занят совместимостью.</summary>
        [Route("version/build")]
        public IActionResult VersionBuild()
        {
            return Json(new Dictionary<string, string>
            {
                ["protocol"] = ProtocolVersion,
                ["version"] = VersionInfo.Version,
                ["gitSha"] = VersionInfo.GitSha,
                ["gitBranch"] = VersionInfo.GitBranch,
                ["buildDate"] = VersionInfo.BuildDate
            });
        }

        [Route("lastupdatedb")]
        public IActionResult LastUpdateDB()
        {
            string lastUpdate = "01.01.2000 01:01";
            if (FileDB.masterDb != null && FileDB.masterDb.Count > 0)
                lastUpdate = FileDB.masterDb.OrderByDescending(i => i.Value.updateTime).First().Value.updateTime.ToString("dd.MM.yyyy HH:mm");

            return Json(new Dictionary<string, string>
            {
                ["lastupdatedb"] = lastUpdate
            });
        }

        [Route("api/v1.0/conf")]
        public JsonResult JacBlackConf([FromQuery] string apikey = null)
        {
            var provided = !string.IsNullOrWhiteSpace(apikey)
                ? apikey.Trim()
                : JacBlackKeyUtils.GetApiKeyFromRequest(HttpContext);
            var configured = AppInit.conf?.apikey;
            return Json(new
            {
                apikey = string.IsNullOrWhiteSpace(configured) || JacBlackKeyUtils.SecureEquals(provided, configured)
            });
        }
    }
}
