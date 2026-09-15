using JacBlack.Infrastructure.Networking;
using Xunit;

namespace JacBlack.Tests.Networking;

/// <summary>
/// Имена сессий браузера и уборка сирот.
///
/// 15.09.2026 у FlareSolverr висело 28 сессий — по целому Chrome на каждую,
/// 270% процессора из 400. Имя сессии выхода строилось через
/// `string.GetHashCode()`, который в .NET случаен на каждый запуск, и после
/// перезапуска службы прежние браузеры никто уже не мог ни найти, ни закрыть.
/// </summary>
public class BrowserSessionNameTests
{
    const string Выход = "socks5://egress:1083";

    [Fact]
    public void Без_выхода_имя_общее()
    {
        Assert.Equal("jacblack", CloudflareClearance.SessionNameFor(null));
    }

    [Fact]
    public void Имя_выхода_не_зависит_от_запуска()
    {
        // Значение зашито нарочно: SHA-256 одинаков в любом процессе, и если
        // кто-то вернёт случайный хеш, тест упадёт, а не пройдёт по совпадению
        // двух вызовов внутри одного процесса.
        Assert.Equal("jacblack-c3a1a0bc0e", CloudflareClearance.SessionNameFor(Выход));
        Assert.Equal(CloudflareClearance.SessionNameFor(Выход), CloudflareClearance.SessionNameFor(" " + Выход + " "));
    }

    [Fact]
    public void Разные_выходы_разные_сессии()
    {
        Assert.NotEqual(
            CloudflareClearance.SessionNameFor("socks5://egress:1083"),
            CloudflareClearance.SessionNameFor("socks5://egress:1085"));
    }

    [Fact]
    public void Адрес_выхода_в_имя_не_попадает()
    {
        string name = CloudflareClearance.SessionNameFor("socks5://user:secret@egress:1083");
        Assert.DoesNotContain("secret", name);
        Assert.DoesNotContain("egress", name);
    }

    [Fact]
    public void Сироты_это_наши_имена_которые_нынешний_конфиг_дать_не_может()
    {
        string свой = CloudflareClearance.SessionNameFor(Выход);
        var live = new[]
        {
            "jacblack",             // общая сессия — своя
            свой,                   // выход из конфига — своя
            "jacblack-1795913469",  // старое случайное имя прежнего запуска
            CloudflareClearance.SessionNameFor("socks5://egress:1099"), // выход убран из конфига
            "someone-else",         // чужая сессия — не наша забота
            null
        };

        var orphans = CloudflareClearance.OrphanSessions(live, new[] { Выход, "", null });

        Assert.Equal(new[] { "jacblack-1795913469", CloudflareClearance.SessionNameFor("socks5://egress:1099") }, orphans);
    }

    [Fact]
    public void Пустой_список_сирот_не_даёт()
    {
        Assert.Empty(CloudflareClearance.OrphanSessions(null, null));
        Assert.Empty(CloudflareClearance.OrphanSessions(new[] { "jacblack" }, null));
    }
}
