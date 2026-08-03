using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.IO;

namespace JacBlack.Infrastructure.Logging
{
    /// <summary>Writes log message as-is (JacBlackLog embeds category prefix in the line).</summary>
    public sealed class JacBlackConsoleFormatter : ConsoleFormatter
    {
        public const string FormatterName = "jacred";

        public JacBlackConsoleFormatter() : base(FormatterName) { }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
        {
            var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
            if (string.IsNullOrEmpty(message)) return;
            textWriter.WriteLine(message);
        }
    }

    public sealed class JacBlackConsoleFormatterConfigureOptions : IConfigureOptions<ConsoleLoggerOptions>
    {
        public void Configure(ConsoleLoggerOptions options)
        {
            options.FormatterName = JacBlackConsoleFormatter.FormatterName;
        }
    }
}
