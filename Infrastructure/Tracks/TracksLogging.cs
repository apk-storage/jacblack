using JacBlack.Infrastructure.Logging;
using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace JacBlack.Infrastructure.Tracks
{
    internal static class TracksLogging
    {
        internal static void Log(string message, int? typetask = null, LogLevel? level = null)
        {
            var logLevel = level ?? JacBlackLog.ClassifyTracksMessage(message);
            if (logLevel == LogLevel.Debug && !JacBlackLogSettings.TracksConsoleDetail)
            {
                if (AppInit.conf?.trackslog == true)
                    LogToFile(message, typetask);
                return;
            }

            if (!JacBlackLogSettings.TracksConsoleDetail && logLevel == LogLevel.Warning
                && !message.Contains("без результата", StringComparison.Ordinal))
            {
                if (AppInit.conf?.trackslog == true)
                    LogToFile(message, typetask);
                return;
            }

            string timeNow = DateTime.Now.ToString("HH:mm:ss");
            string typetaskInfo = typetask.HasValue ? $" [task:{typetask.Value}]" : "";
            string body = $"[{timeNow}]{typetaskInfo} {message}";

            JacBlackLog.Write(JacBlackLogCategories.Tracks, logLevel, body);

            if (AppInit.conf?.trackslog == true)
                LogToFile(message, typetask);
        }

        internal static void LogToFile(string message, int? typetask = null)
        {
            try
            {
                string logDir = "Data/log";
                string logFile = Path.Combine(logDir, "tracks.log");

                Directory.CreateDirectory(logDir);

                string timeNow = DateTime.Now.ToString("HH:mm:ss");
                string typetaskInfo = typetask.HasValue ? $" [task:{typetask.Value}]" : "";
                string logMessage = $"tracks: [{timeNow}]{typetaskInfo} {message}{Environment.NewLine}";

                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        using (var stream = new FileStream(
                            logFile,
                            FileMode.Append,
                            FileAccess.Write,
                            FileShare.ReadWrite))
                        using (var writer = new StreamWriter(stream, Encoding.UTF8))
                        {
                            writer.Write(logMessage);
                        }
                        break;
                    }
                    catch (IOException) when (i < 2)
                    {
                        Thread.Sleep(50);
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string timeNow = DateTime.Now.ToString("HH:mm:ss");
                    JacBlackLog.Error(JacBlackLogCategories.Tracks, $"[{timeNow}] Ошибка записи в лог файл: {ex.Message}");
                }
                catch
                {
                    // Сообщить о сбое записи в лог тоже не вышло — писать больше некуда.
                    // Единственное место, где пустой catch уместен по существу.
                }
            }
        }
    }
}
