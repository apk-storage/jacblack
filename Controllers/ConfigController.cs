using System;
using JacRed;
using JacRed.Engine;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace JacRed.Controllers
{
    public class ConfigSaveRequest
    {
        public string content { get; set; }
        public string format { get; set; }
        public object data { get; set; }
    }

    [Route("api/v1.0/config")]
    public class ConfigController : Controller
    {
        [HttpGet("schema")]
        public IActionResult Schema()
        {
            return Json(new { ok = true, schema = ConfigSchema.Get() });
        }

        [HttpGet("")]
        public IActionResult Get([FromQuery] string format = null, [FromQuery] bool raw = false)
        {
            var info = AppInit.GetConfigSourceInfo();
            var fmt = format ?? info.format ?? "yaml";
            var data = AppInit.GetConfigData(redactSensitive: !raw);
            var content = AppInit.GetConfigContent(redactSensitive: !raw, format: fmt);

            return Json(new
            {
                ok = true,
                path = info.path,
                format = info.format,
                displayFormat = fmt,
                exists = info.exists,
                lastModifiedUtc = info.lastModifiedUtc,
                data,
                content,
                schema = ConfigSchema.Get(),
                examplePath = System.IO.File.Exists("Data/example.yaml") ? "Data/example.yaml" : "Data/example.conf",
                sensitiveFields = ConfigSchema.SensitiveFieldNames,
                note = raw
                    ? "Полный конфиг с секретами. Доступ только из локальной сети."
                    : "Секретные поля заменены на ***. При сохранении *** сохраняет текущие значения."
            });
        }

        [HttpPost("validate")]
        public IActionResult Validate([FromBody] ConfigSaveRequest body)
        {
            if (body == null)
                return Json(new { ok = false, error = "Тело запроса пусто" });

            var (jo, parseError) = AppInit.TryParseRequestToJObject(body.content, body.format, body.data);
            if (jo == null)
                return Json(new { ok = false, error = parseError });

            var result = AppInit.ValidateConfigObject(jo);
            return Json(new
            {
                ok = result.ok,
                error = result.error,
                errors = result.errors,
                warnings = result.warnings
            });
        }

        [HttpPost("diff")]
        public IActionResult Diff([FromBody] ConfigSaveRequest body)
        {
            if (body == null)
                return Json(new { ok = false, error = "Тело запроса пусто" });

            var (proposed, parseError) = AppInit.TryParseRequestToJObject(body.content, body.format, body.data);
            if (proposed == null)
                return Json(new { ok = false, error = parseError });

            var validation = AppInit.ValidateConfigObject(proposed);
            var diffs = AppInit.ComputeConfigDiff(proposed);

            return Json(new
            {
                ok = true,
                diffs,
                changeCount = diffs.Count,
                validation = new
                {
                    ok = validation.ok,
                    error = validation.error,
                    errors = validation.errors,
                    warnings = validation.warnings
                }
            });
        }

        [HttpPost("")]
        public IActionResult Save([FromBody] ConfigSaveRequest body)
        {
            if (body == null)
                return Json(new { ok = false, error = "Тело запроса пусто" });

            (bool ok, string error, AppInit.ConfigSourceInfo info) result;
            if (body.data != null)
            {
                var jo = body.data is JObject j ? j : JObject.FromObject(body.data);
                result = AppInit.SaveConfigObject(jo, body.format);
            }
            else if (!string.IsNullOrWhiteSpace(body.content))
            {
                result = AppInit.SaveConfigContent(body.content, body.format);
            }
            else
            {
                return Json(new { ok = false, error = "Укажите data или content" });
            }

            if (!result.ok)
                return Json(new { ok = false, error = result.error });

            return Json(new
            {
                ok = true,
                path = result.info?.path,
                format = result.info?.format,
                lastModifiedUtc = result.info?.lastModifiedUtc,
                message = "Конфигурация сохранена. Изменения применятся автоматически."
            });
        }
    }
}
