using JacBlack.Infrastructure.Parsing;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Сложение имён трекеров у склеенной раздачи.
///
/// Раньше складывались СТРОКИ и сравнивались подстрокой: «bitru, kinozal» не
/// содержит «kinozal, rutor», поэтому вторая приписывалась целиком и выходило
/// «bitru, kinozal, kinozal, rutor». Замер 10.09.2026 на живом запросе по
/// «Дюне»: 43 строки из 107 с повтором.
/// </summary>
public class TrackerNamesMergeTests
{
    [Fact]
    public void Общее_имя_не_повторяется()
    {
        Assert.Equal("bitru, kinozal, rutor", TrackerNames.Merge("bitru, kinozal", "kinozal, rutor"));
    }

    [Fact]
    public void Порядок_сохраняется_а_новое_идёт_в_конец()
    {
        Assert.Equal("rutor, kinozal, bitru", TrackerNames.Merge("rutor, kinozal", "bitru"));
    }

    [Fact]
    public void Регистр_не_плодит_второе_имя()
    {
        Assert.Equal("Rutracker", TrackerNames.Merge("Rutracker", "rutracker"));
    }

    [Fact]
    public void Пустая_вторая_часть_нормализует_первую()
    {
        Assert.Equal("bitru, kinozal, rutor",
            TrackerNames.Merge("bitru, kinozal, kinozal, rutor, bitru", null));
    }

    [Fact]
    public void Пустое_поле_не_роняет()
    {
        Assert.Equal("kinozal", TrackerNames.Merge(null, "kinozal"));
        Assert.Equal("kinozal", TrackerNames.Merge("kinozal", ""));
        Assert.Null(TrackerNames.Merge(null, null));
    }
}
