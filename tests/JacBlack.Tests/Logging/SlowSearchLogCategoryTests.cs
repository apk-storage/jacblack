using System.Collections.Generic;
using JacBlack.Configuration;
using JacBlack.Infrastructure.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JacBlack.Tests.Logging;

/// <summary>
/// Запись «долгий поиск» должна доходить до журнала при обычном конфиге.
///
/// С 14.09.2026 она писалась в категорию parser, а шаблон конфига гасит её
/// целиком (`parsers: None`), — раскладка по этапам не появилась ни разу, и
/// причину медленного поиска пришлось искать вслепую.
/// </summary>
public class SlowSearchLogCategoryTests
{
    [Fact]
    public void Категория_поиска_не_гасится_вместе_с_парсерами()
    {
        var conf = new AppOptions
        {
            logging = new LoggingOptions
            {
                categories = new Dictionary<string, string> { ["parsers"] = "None" }
            }
        };

        try
        {
            JacBlackLogSettings.Apply(conf);

            Assert.False(JacBlackLogSettings.IsEnabled(JacBlackLogCategories.Parser, LogLevel.Warning));
            Assert.True(JacBlackLogSettings.IsEnabled(JacBlackLogCategories.Search, LogLevel.Warning));
        }
        finally
        {
            JacBlackLogSettings.Apply(AppInit.conf);
        }
    }

    [Fact]
    public void Без_секции_logging_категория_поиска_тоже_пишется()
    {
        try
        {
            JacBlackLogSettings.Apply(new AppOptions { logging = null });
            Assert.True(JacBlackLogSettings.IsEnabled(JacBlackLogCategories.Search, LogLevel.Warning));
        }
        finally
        {
            JacBlackLogSettings.Apply(AppInit.conf);
        }
    }
}
