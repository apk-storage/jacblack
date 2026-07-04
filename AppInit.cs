using JacRed.Models;
using JacRed.Models.AppConf;
using JacRed.Engine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace JacRed
{
    public class AppInit
    {
        private static readonly HashSet<string> SensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apikey", "devkey", "cookie", "u", "p", "username", "password"
        };

        /// <summary>
        /// Returns current configuration as JSON with sensitive values (apikey, cookie, login, proxy auth) redacted.
        /// </summary>
        public static string GetSafeConfigJson()
        {
            var c = conf;
            if (c == null) return "{}";
            var jo = JObject.FromObject(c);
            RedactSensitive(jo);
            return jo.ToString(Formatting.Indented);
        }

        private static void RedactSensitive(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties().ToList())
                {
                    if (SensitiveKeys.Contains(prop.Name) && prop.Value != null && prop.Value.Type != JTokenType.Null && prop.Value.Type != JTokenType.Undefined)
                    {
                        var val = prop.Value.ToString();
                        if (!string.IsNullOrEmpty(val))
                            prop.Value = "***";
                    }
                    else
                        RedactSensitive(prop.Value);
                }
            }
            else if (token is JArray arr)
            {
                foreach (var item in arr)
                    RedactSensitive(item);
            }
        }

        private static void LogSafeConfig(string label, string source = null)
        {
            try
            {
                var src = string.IsNullOrEmpty(source) ? "" : $" from {source}";
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {label}{src} applied (sensitive data redacted):");
                Console.WriteLine(GetSafeConfigJson());
            }
            catch { }
        }

        private const string ConfigFileYaml = "init.yaml";
        private const string ConfigFileJson = "init.conf";

        /// <summary>
        /// Config file priority: init.yaml wins over init.conf. If both exist, init.yaml is used.
        /// </summary>
        private static (string path, DateTime lastWrite) GetConfigSource()
        {
            var hasYaml = File.Exists(ConfigFileYaml);
            var hasJson = File.Exists(ConfigFileJson);
            if (hasYaml)
                return (ConfigFileYaml, File.GetLastWriteTimeUtc(ConfigFileYaml));
            if (hasJson)
                return (ConfigFileJson, File.GetLastWriteTimeUtc(ConfigFileJson));
            return (null, default);
        }

        private static AppInit LoadConfigFromFile(string path)
        {
            var text = File.ReadAllText(path);
            if (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                var deserializer = new DeserializerBuilder().Build();
                var yamlObj = deserializer.Deserialize<object>(new StringReader(text));
                var json = JsonConvert.SerializeObject(yamlObj);
                return JsonConvert.DeserializeObject<AppInit>(json);
            }
            return JsonConvert.DeserializeObject<AppInit>(text);
        }

        #region AppInit
        static AppInit()
        {
            void updateConf()
            {
                try
                {
                    var (path, lastWrite) = GetConfigSource();

                    if (cacheconf.Item1 == null)
                    {
                        if (path == null)
                        {
                            cacheconf.Item1 = new AppInit();
                            cacheconf.Item2 = null;
                            cacheconf.Item3 = default;
                            LogSafeConfig("config (default)");
                            return;
                        }
                        cacheconf.Item1 = LoadConfigFromFile(path);
                        cacheconf.Item2 = path;
                        cacheconf.Item3 = lastWrite;
                        LogSafeConfig("config (start)", path);
                        return;
                    }

                    if (path == null)
                        return;

                    if (cacheconf.Item2 != path || cacheconf.Item3 != lastWrite)
                    {
                        bool isReload = cacheconf.Item2 != null;
                        cacheconf.Item1 = LoadConfigFromFile(path);
                        cacheconf.Item2 = path;
                        cacheconf.Item3 = lastWrite;
                        LogSafeConfig(isReload ? "config (reload)" : "config (start)", path);
                    }
                }
                catch { }
            }

            updateConf();

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    updateConf();
                }
            });
        }

        static (AppInit, string path, DateTime lastWrite) cacheconf = default;

        public static AppInit conf => cacheconf.Item1;

        // Parser log is written only when parser log is enabled and this tracker's log is true.
        public static bool TrackerLogEnabled(string trackerName)
        {
            bool parserLogEnabled = conf?.logParsers == true || conf?.log == true;
            if (!parserLogEnabled || string.IsNullOrWhiteSpace(trackerName))
                return false;
            switch (trackerName.ToLowerInvariant())
            {
                case "anidub": return conf.Anidub.log;
                case "aniliberty": return conf.Aniliberty.log;
                case "animelayer": return conf.Animelayer.log;
                case "baibako": return conf.Baibako.log;
                case "bitru": return conf.Bitru.log;
                case "knaben": return conf.Knaben.log;
                case "kinozal": return conf.Kinozal.log;
                case "lostfilm": return conf.Lostfilm.log;
                case "mazepa": return conf.Mazepa.log;
                case "megapeer": return conf.Megapeer.log;
                case "nnmclub": return conf.NNMClub.log;
                case "rutor": return conf.Rutor.log;
                case "rutracker": return conf.Rutracker.log;
                case "selezen": return conf.Selezen.log;
                case "toloka": return conf.Toloka.log;
                case "torrentby": return conf.TorrentBy.log;
                default: return false;
            }
        }
        #endregion


        public string listenip = "any";

        public int listenport = 9117;

        public string apikey = null;

        /// <summary>Если задан — доступ к /dev/, /cron/, /jsondb только с заголовком X-Dev-Key или параметром devkey (нужно за туннелем/прокси, когда все запросы выглядят локальными).</summary>
        public string devkey = null;

        public bool mergeduplicates = true;

        public bool mergenumduplicates = true;

        public bool openstats = true;

        public bool opensync = true;

        public bool opensync_v1 = false;

        public bool tracks = false;

        public bool web = true;

        /// <summary>
        /// 0 - все
        /// 1 - день, месяц
        /// </summary>
        public int tracksmod = 0;

        public int tracksdelay = 20_000;

        public bool trackslog = false;

        public int tracksatempt = 20;

        public string trackscategory = "jacred";

        public class TracksIntervalConfig
        {
            public int task0 { get; set; } = 180;
            public int task1 { get; set; } = 60;
        }

        [JsonProperty("tracksinterval")]
        public TracksIntervalConfig TracksInterval { get; set; } = new TracksIntervalConfig();

        public static class TracksIntervalStatic
        {
            public static int task0 => conf?.TracksInterval?.task0 ?? 180;
            public static int task1 => conf?.TracksInterval?.task1 ?? 60;
        }

        public string[] tsuri = new string[] { "http://127.0.0.1:8090" };

        // Deprecated: use logFdb and logParsers. When true, enables both fdb and parser logs for backward compatibility.
        public bool log = false;

        // When true, write FileDB add/update entries to Data/log/fdb.YYYY-MM-DD.log as JSON Lines (one JSON array per line; subject to retention/size/file limits).
        public bool logFdb = false;

        // Keep fdb log files only for this many days (0 = keep all). Applied when logFdb is true.
        public int logFdbRetentionDays = 7;

        // Max total size of fdb log files in MB (0 = no limit). Oldest files are deleted first.
        public int logFdbMaxSizeMb = 0;

        // Max number of fdb log files to keep (0 = no limit). Oldest files are deleted first.
        public int logFdbMaxFiles = 0;

        // When true, parsers write to Data/log/{tracker}.log for trackers that have log enabled in their settings.
        public bool logParsers = false;

        public string syncapi = null;

        public string[] synctrackers = null;

        public string[] disable_trackers = new string[] { };

        public bool syncsport = true;

        public bool syncspidr = true;

        public int maxreadfile = 200;

        public Evercache evercache = new Evercache() { enable = true, validHour = 1, maxOpenWriteTask = 2000, dropCacheTake = 200 };

        public int fdbPathLevels = 2;

        public int timeStatsUpdate = 90; // минут

        public int timeSync = 60; // минут

        public int timeSyncSpidr = 60; // минут (30, 60, 120 — без случайного смещения)

        public TrackerSettings Rutor = new TrackerSettings("http://rutor.info");

        public TrackerSettings Megapeer = new TrackerSettings("http://megapeer.vip");

        public TrackerSettings TorrentBy = new TrackerSettings("https://torrent.by");

        public TrackerSettings Kinozal = new TrackerSettings("https://kinozal.tv");

        public TrackerSettings NNMClub = new TrackerSettings("https://nnmclub.to");

        public TrackerSettings Bitru = new TrackerSettings("https://bitru.org");

        public TrackerSettings Toloka = new TrackerSettings("https://toloka.to");

        public TrackerSettings Mazepa = new TrackerSettings("https://mazepa.to");

        public TrackerSettings Rutracker = new TrackerSettings("https://rutracker.org");

        public TrackerSettings Selezen = new TrackerSettings("https://use.selezen.club");

        public TrackerSettings Lostfilm = new TrackerSettings("https://www.lostfilm.tv");

        public TrackerSettings Animelayer = new TrackerSettings("https://animelayer.ru");

        public TrackerSettings Anidub = new TrackerSettings("https://tr.anidub.com");

        public TrackerSettings Aniliberty = new TrackerSettings("https://aniliberty.top");

        public TrackerSettings Knaben = new TrackerSettings("https://api.knaben.org");

        // TODO: fix parser
        public TrackerSettings Baibako = new TrackerSettings("http://baibako.tv");

        public ProxySettings proxy = new ProxySettings();

        public TorznabSettings torznab = new TorznabSettings();

        public List<ProxySettings> globalproxy;

        #region ConfigManagement
        public sealed class ConfigSourceInfo
        {
            public string path { get; set; }
            public string format { get; set; }
            public bool exists { get; set; }
            public DateTime? lastModifiedUtc { get; set; }
        }

        public sealed class ConfigValidationResult
        {
            public bool ok { get; set; }
            public string error { get; set; }
            public List<string> warnings { get; set; } = new List<string>();
            public List<string> errors { get; set; } = new List<string>();
        }

        private const string RedactedPlaceholder = "***";
        public const string ConfigRedactedPlaceholder = "***";

        public static ConfigSourceInfo GetConfigSourceInfo()
        {
            var (path, lastWrite) = GetConfigSource();
            return new ConfigSourceInfo
            {
                path = path,
                format = path == null ? null : (path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ? "yaml" : "json"),
                exists = path != null,
                lastModifiedUtc = path == null ? (DateTime?)null : lastWrite
            };
        }

        public static JObject GetConfigData(bool redactSensitive = true)
        {
            var c = conf;
            if (c == null) return new JObject();
            var jo = JObject.FromObject(c);
            if (redactSensitive)
                RedactSensitive(jo);
            return jo;
        }

        public static string GetConfigContent(bool redactSensitive = true, string format = null)
        {
            var c = conf;
            if (c == null) return format == "json" ? "{}" : "---\n";

            var jo = JObject.FromObject(c);
            if (redactSensitive)
                RedactSensitive(jo);

            return SerializeConfigObject(jo, format ?? GetConfigSourceInfo().format ?? "yaml");
        }

        public static (JObject data, string error) TryParseRequestToJObject(string content, string format, object dataObj)
        {
            if (dataObj != null)
            {
                try
                {
                    var jo = dataObj is JObject j ? j : JObject.FromObject(dataObj);
                    return (jo, null);
                }
                catch (Exception ex)
                {
                    return (null, ex.Message);
                }
            }

            if (string.IsNullOrWhiteSpace(content))
                return (null, "Укажите data или content");

            var fmt = format ?? DetectConfigFormat(content);
            var parsed = TryParseConfigContent(content, fmt, out var error);
            if (parsed == null)
                return (null, error ?? "Не удалось разобрать конфигурацию");

            return (JObject.FromObject(parsed), null);
        }

        public static ConfigValidationResult ValidateConfigObject(JObject data)
        {
            var result = new ConfigValidationResult();
            if (data == null)
            {
                result.error = "Данные конфигурации пусты";
                return result;
            }

            try
            {
                var parsed = data.ToObject<AppInit>();
                return ValidateConfigModel(parsed);
            }
            catch (Exception ex)
            {
                result.error = ex.Message;
                return result;
            }
        }

        public static ConfigValidationResult ValidateConfigContent(string content, string format)
        {
            var result = new ConfigValidationResult();
            if (string.IsNullOrWhiteSpace(content))
            {
                result.error = "Конфигурация пуста";
                return result;
            }

            try
            {
                var parsed = TryParseConfigContent(content, format, out var error);
                if (parsed == null)
                {
                    result.error = error ?? "Не удалось разобрать конфигурацию";
                    return result;
                }

                return ValidateConfigModel(parsed);
            }
            catch (Exception ex)
            {
                result.error = ex.Message;
            }

            return result;
        }

        private static ConfigValidationResult ValidateConfigModel(AppInit parsed)
        {
            var result = new ConfigValidationResult();
            ConfigSchema.ValidateAgainstSchema(parsed, result.errors, result.warnings);
            result.ok = result.errors.Count == 0;
            if (!result.ok)
                result.error = result.errors[0];
            return result;
        }

        public static List<ConfigDiffEntry> ComputeConfigDiff(JObject proposed, bool redactSensitive = true)
        {
            var current = JObject.FromObject(conf ?? new AppInit());
            var merged = (JObject)proposed.DeepClone();
            MergeSensitiveTokens(merged, JObject.FromObject(conf ?? new AppInit()));

            if (redactSensitive)
            {
                RedactSensitive(current);
                RedactSensitive(merged);
            }

            return ConfigSchema.ComputeDiff(current, merged);
        }

        public static (bool ok, string error, ConfigSourceInfo info) SaveConfigObject(JObject data, string format = null)
        {
            if (data == null)
                return (false, "Данные конфигурации пусты", null);

            try
            {
                var parsed = data.ToObject<AppInit>();
                if (parsed == null)
                    return (false, "Не удалось преобразовать конфигурацию", null);

                var validation = ValidateConfigModel(parsed);
                if (!validation.ok)
                    return (false, validation.error ?? "Ошибка валидации", null);

                var merged = MergeSensitiveFromCurrent(parsed);
                var jo = JObject.FromObject(merged);
                return SaveConfigObjectInternal(jo, format);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, null);
            }
        }

        public static (bool ok, string error, ConfigSourceInfo info) SaveConfigContent(string content, string format = null)
        {
            if (string.IsNullOrWhiteSpace(content))
                return (false, "Конфигурация пуста", null);

            var sourceInfo = GetConfigSourceInfo();
            var outputFormat = format ?? sourceInfo.format ?? "yaml";

            var parsed = TryParseConfigContent(content, DetectFormat(content, outputFormat), out var parseError);
            if (parsed == null)
                return (false, parseError ?? "Не удалось разобрать конфигурацию", sourceInfo);

            var validation = ValidateConfigModel(parsed);
            if (!validation.ok)
                return (false, validation.error ?? "Ошибка валидации", sourceInfo);

            var merged = MergeSensitiveFromCurrent(parsed);
            var jo = JObject.FromObject(merged);
            return SaveConfigObjectInternal(jo, format);
        }

        private static (bool ok, string error, ConfigSourceInfo info) SaveConfigObjectInternal(JObject jo, string format)
        {
            var sourceInfo = GetConfigSourceInfo();
            var outputFormat = format ?? sourceInfo.format ?? "yaml";
            var targetPath = sourceInfo.path ?? (outputFormat == "json" ? ConfigFileJson : ConfigFileYaml);

            try
            {
                var serialized = SerializeConfigObject(jo, outputFormat);
                WriteConfigAtomically(targetPath, serialized);
                ReloadConfigFromDisk(targetPath);
                return (true, null, GetConfigSourceInfo());
            }
            catch (Exception ex)
            {
                return (false, ex.Message, sourceInfo);
            }
        }

        private static AppInit TryParseConfigContent(string content, string format, out string error)
        {
            error = null;
            try
            {
                if (string.Equals(format, "yaml", StringComparison.OrdinalIgnoreCase))
                {
                    var deserializer = new DeserializerBuilder().Build();
                    var yamlObj = deserializer.Deserialize<object>(new StringReader(content));
                    var json = JsonConvert.SerializeObject(yamlObj);
                    return JsonConvert.DeserializeObject<AppInit>(json);
                }

                return JsonConvert.DeserializeObject<AppInit>(content);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public static string DetectConfigFormat(string content, string fallback = "yaml")
        {
            if (string.IsNullOrWhiteSpace(content)) return fallback;
            var trimmed = content.TrimStart();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                return "json";
            if (trimmed.StartsWith("---") || trimmed.Contains(':'))
                return "yaml";
            return fallback;
        }

        private static string DetectFormat(string content, string fallback)
            => DetectConfigFormat(content, fallback);

        private static AppInit MergeSensitiveFromCurrent(AppInit incoming)
        {
            var current = conf;
            if (current == null || incoming == null)
                return incoming;

            var incomingJo = JObject.FromObject(incoming);
            var currentJo = JObject.FromObject(current);
            MergeSensitiveTokens(incomingJo, currentJo);
            return incomingJo.ToObject<AppInit>();
        }

        private static void MergeSensitiveTokens(JObject incoming, JObject current)
        {
            foreach (var prop in incoming.Properties().ToList())
            {
                if (prop.Value == null || prop.Value.Type == JTokenType.Null)
                    continue;

                if (prop.Value.Type == JTokenType.String && prop.Value.ToString() == RedactedPlaceholder)
                {
                    var curVal = current[prop.Name];
                    if (curVal != null && curVal.Type != JTokenType.Null)
                        prop.Value = curVal.DeepClone();
                    continue;
                }

                if (prop.Value is JObject incObj && current[prop.Name] is JObject curObj)
                    MergeSensitiveTokens(incObj, curObj);
            }
        }

        private static void CollectValidationWarnings(AppInit config, List<string> warnings)
        {
            if (config == null)
            {
                warnings.Add("Конфигурация не задана");
                return;
            }

            if (config.listenport < 1 || config.listenport > 65535)
                warnings.Add("listenport должен быть в диапазоне 1–65535");

            if (config.tracksmod != 0 && config.tracksmod != 1)
                warnings.Add("tracksmod: допустимы значения 0 или 1");

            if (config.timeSync < 1)
                warnings.Add("timeSync должен быть больше 0");

            if (config.timeStatsUpdate < 1)
                warnings.Add("timeStatsUpdate должен быть больше 0");

            if (config.maxreadfile < 1)
                warnings.Add("maxreadfile должен быть больше 0");

            if (config.fdbPathLevels < 1 || config.fdbPathLevels > 4)
                warnings.Add("fdbPathLevels: рекомендуется 1–4");
        }

        private static string SerializeConfigObject(JObject jo, string format)
        {
            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                return jo.ToString(Formatting.Indented);

            var serializer = new SerializerBuilder()
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();
            var obj = JsonConvert.DeserializeObject<object>(jo.ToString(Formatting.None));
            using var writer = new StringWriter();
            writer.WriteLine("---");
            serializer.Serialize(writer, obj);
            return writer.ToString();
        }

        private static void WriteConfigAtomically(string path, string content)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        private static void ReloadConfigFromDisk(string path)
        {
            cacheconf.Item1 = LoadConfigFromFile(path);
            cacheconf.Item2 = path;
            cacheconf.Item3 = File.GetLastWriteTimeUtc(path);
            LogSafeConfig("config (saved)", path);
        }
        #endregion
    }
}
