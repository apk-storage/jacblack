using JacBlack.Models.AppConf;
using Xunit;

namespace JacBlack.Tests;

/// <summary>
/// Пауза между страницами обхода считается из `reqMinute`.
///
/// До 16.09.2026 при `reqMinute >= 60` пауза жёстко равнялась секунде, то есть
/// быстрее шестидесяти страниц в минуту обход не мог идти в принципе. У
/// rutracker это стало заметно: страница в браузере занимает 2,5 с, браузер
/// занят 78% времени, остальное — наша пауза.
/// </summary>
public class ParseDelayTests
{
    static int Delay(int reqMinute) => new TrackerSettings("https://example.org", reqMinute: reqMinute).parseDelay;

    [Theory]
    [InlineData(8, 7500)]
    [InlineData(12, 5000)]
    [InlineData(20, 3000)]
    [InlineData(30, 2000)]
    [InlineData(60, 1000)]
    public void Прежние_значения_не_изменились(int reqMinute, int ожидалось)
    {
        Assert.Equal(ожидалось, Delay(reqMinute));
    }

    [Theory]
    [InlineData(120, 500)]
    [InlineData(240, 250)]
    public void Выше_шестидесяти_пауза_наконец_уменьшается(int reqMinute, int ожидалось)
    {
        Assert.Equal(ожидалось, Delay(reqMinute));
    }

    [Fact]
    public void Ниже_ста_миллисекунд_не_опускаемся()
    {
        Assert.Equal(100, Delay(600));
        Assert.Equal(100, Delay(100000));
    }

    [Fact]
    public void Особые_значения_как_были()
    {
        Assert.Equal(10, Delay(-1));      // «без паузы»
        Assert.Equal(60_000, Delay(0));   // выключено: раз в минуту
    }
}
