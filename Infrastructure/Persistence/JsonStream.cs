using JacRed.Infrastructure.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;

namespace JacRed.Infrastructure.Persistence
{
    public static class JsonStream
    {
        /// <summary>
        /// Замки на файл, а не один на процесс. Раньше здесь был единственный
        /// static object, через который проходила запись ВСЕХ 250 тысяч шардов —
        /// то есть запись была строго однопоточной, сколько бы парсеров
        /// ни работало параллельно.
        /// </summary>
        static readonly ConcurrentDictionary<string, object> _fileLocks = new();

        static object LockFor(string path) => _fileLocks.GetOrAdd(path, _ => new object());

        #region Read
        public static T Read<T>(string path)
        {
            try
            {
                var settings = new JsonSerializerSettings
                {
                    Error = (se, ev) => { ev.ErrorContext.Handled = true; }
                };

                var serializer = JsonSerializer.Create(settings);

                using (Stream file = new GZipStream(File.OpenRead(path), CompressionMode.Decompress))
                {
                    using (var sr = new StreamReader(file))
                    {
                        using (var jsonTextReader = new JsonTextReader(sr))
                        {
                            return serializer.Deserialize<T>(jsonTextReader);
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {
                // Штатная ситуация: шарда ещё нет.
                return default;
            }
            catch (DirectoryNotFoundException)
            {
                return default;
            }
            catch (Exception ex)
            {
                // Раньше сюда попадал ЛЮБОЙ сбой и молча возвращалась пустота:
                // повреждённый файл был неотличим от несуществующего, и данные
                // терялись без единого сообщения. Теперь файл уводится
                // в карантин, а не переписывается поверх пустым содержимым.
                Quarantine(path, ex);
                return default;
            }
        }

        static void Quarantine(string path, Exception ex)
        {
            try
            {
                JacRedLog.Error(JacRedLogCategories.Fdb,
                    $"шард повреждён, уводим в карантин: {path} — {ex.GetType().Name}: {ex.Message}");

                string dir = Path.Combine("Data", "corrupt");
                Directory.CreateDirectory(dir);

                string dest = Path.Combine(dir, Path.GetFileName(path) + "." + DateTime.UtcNow.Ticks);
                if (File.Exists(path))
                    File.Move(path, dest, overwrite: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        #endregion

        #region Write
        public static void Write(string path, object db)
        {
            lock (LockFor(path))
            {
                string tempPath = path + ".tmp";

                try
                {
                    var serializer = JsonSerializer.Create();

                    using (var sw = new StreamWriter(new GZipStream(File.Create(tempPath), CompressionMode.Compress)))
                    {
                        using (var jsonTextWriter = new JsonTextWriter(sw))
                        {
                            serializer.Serialize(jsonTextWriter, db);
                        }
                    }

                    if (File.Exists(path))
                        File.Replace(tempPath, path, null);
                    else
                        File.Move(tempPath, path);
                }
                catch (Exception ex)
                {
                    // Раньше сбой записи проглатывался целиком — потеря данных
                    // выглядела как обычная работа.
                    JacRedLog.Error(JacRedLogCategories.Fdb,
                        $"не удалось записать шард {path} — {ex.GetType().Name}: {ex.Message}");

                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch (IOException) { }
                }
            }
        }
        #endregion
    }
}
