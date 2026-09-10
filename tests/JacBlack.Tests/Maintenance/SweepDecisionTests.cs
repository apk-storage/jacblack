using JacBlack.Application.Maintenance;
using JacBlack.Models.Details;
using Xunit;

namespace JacBlack.Tests.Maintenance;

/// <summary>
/// Что круг живости делает с записью по ответу анонса.
///
/// Главная болезнь базы была тут: нули считались, а число сидов не трогалось —
/// у раздачи, которой давно нет, годами стоял снимок вроде «89 раздающих».
/// Заслон мёртвых на выдаче при этом проверяет «нулей подряд больше порога И
/// сиды не выше нуля», и старое число делало его недостижимым.
/// </summary>
public class SweepDecisionTests
{
    static TorrentDetails Раздача(int sid, int pir = 0, int deadChecks = 0) => new()
    {
        url = "https://rutracker.org/forum/viewtopic.php?t=1",
        sid = sid,
        pir = pir,
        deadChecks = deadChecks,
    };

    [Fact]
    public void Живая_раздача_получает_свежие_числа()
    {
        var t = Раздача(sid: 3, pir: 1, deadChecks: 4);

        var r = SweepDecision.Apply(t, seeders: 17, leechers: 5, deadThreshold: 5);

        Assert.Equal(SweepDecision.Outcome.Alive, r.Outcome);
        Assert.True(r.Revived);
        Assert.Equal(17, t.sid);
        Assert.Equal(5, t.pir);
        Assert.Equal(0, t.deadChecks);
    }

    [Fact]
    public void Первый_ноль_старое_число_не_трогает()
    {
        var t = Раздача(sid: 89, pir: 4);

        var r = SweepDecision.Apply(t, seeders: 0, leechers: 0, deadThreshold: 5);

        Assert.Equal(SweepDecision.Outcome.Zero, r.Outcome);
        Assert.False(r.Zeroed);
        Assert.Equal(89, t.sid);
        Assert.Equal(1, t.deadChecks);
    }

    [Fact]
    public void Второй_ноль_подряд_обнуляет_снимок()
    {
        var t = Раздача(sid: 89, pir: 4, deadChecks: 1);

        var r = SweepDecision.Apply(t, seeders: 0, leechers: 0, deadThreshold: 5);

        Assert.True(r.Zeroed);
        Assert.Equal(0, t.sid);
        Assert.Equal(0, t.pir);
        Assert.Equal(2, t.deadChecks);
    }

    /// <summary>
    /// Обнуление считается один раз: у уже обнулённой раздачи следующий ноль
    /// не должен снова попадать в счётчик отчёта.
    /// </summary>
    [Fact]
    public void Повторный_ноль_обнулением_не_считается()
    {
        var t = Раздача(sid: 0, pir: 0, deadChecks: 3);

        var r = SweepDecision.Apply(t, seeders: 0, leechers: 0, deadThreshold: 5);

        Assert.False(r.Zeroed);
        Assert.Equal(4, t.deadChecks);
    }

    [Fact]
    public void Порог_мёртвых_отмечается_на_нужном_счёте()
    {
        var t = Раздача(sid: 0, deadChecks: 4);

        var r = SweepDecision.Apply(t, seeders: 0, leechers: 0, deadThreshold: 5);

        Assert.True(r.ReachedThreshold);
        Assert.Equal(5, t.deadChecks);
    }

    /// <summary>
    /// Прятать и удалять — разные пороги. Ноль от публичного анонса не
    /// доказывает смерть раздачи, живущей на анонсе своего трекера: замер
    /// 10.09.2026 показал среди «пяти нулей подряд» раздачу rutor с одним
    /// сидом и склейку bitru+nnmclub с девятью.
    /// </summary>
    [Fact]
    public void Прятать_и_удалять_разные_пороги()
    {
        var t = Раздача(sid: 0, deadChecks: 6);

        var r = SweepDecision.Apply(t, seeders: 0, leechers: 0, deadThreshold: 5, deleteThreshold: 40);

        Assert.True(r.ReachedThreshold);
        Assert.False(r.ReachedDeleteThreshold);
    }

    [Fact]
    public void На_своём_пороге_удаление_разрешается()
    {
        var t = Раздача(sid: 0, deadChecks: 39);

        var r = SweepDecision.Apply(t, seeders: 0, leechers: 0, deadThreshold: 5, deleteThreshold: 40);

        Assert.True(r.ReachedDeleteThreshold);
        Assert.Equal(40, t.deadChecks);
    }

    /// <summary>
    /// Порог удаления не может оказаться ниже порога прятания, даже если так
    /// написали в конфиге: иначе запись исчезала бы раньше, чем её успели
    /// хотя бы спрятать.
    /// </summary>
    [Fact]
    public void Порог_удаления_не_опускается_ниже_порога_прятания()
    {
        var t = Раздача(sid: 0, deadChecks: 2);

        var r = SweepDecision.Apply(t, seeders: 0, leechers: 0, deadThreshold: 5, deleteThreshold: 1);

        Assert.False(r.ReachedDeleteThreshold);
    }

    /// <summary>
    /// По умолчанию удаление не разрешается никогда: вызов без явного порога
    /// не должен внезапно начать удалять записи.
    /// </summary>
    [Fact]
    public void Без_указанного_порога_удаление_не_разрешается()
    {
        var t = Раздача(sid: 0, deadChecks: 999);

        var r = SweepDecision.Apply(t, seeders: 0, leechers: 0, deadThreshold: 5);

        Assert.True(r.ReachedThreshold);
        Assert.False(r.ReachedDeleteThreshold);
    }

    /// <summary>
    /// Молчание трекера — это «не знаю», а не «мертва»: ни счётчик, ни числа
    /// трогать нельзя, иначе сетевой сбой утопил бы живую раздачу.
    /// </summary>
    [Fact]
    public void Молчание_трекера_запись_не_меняет()
    {
        var t = Раздача(sid: 42, pir: 7, deadChecks: 1);

        var r = SweepDecision.Apply(t, seeders: null, leechers: null, deadThreshold: 5);

        Assert.Equal(SweepDecision.Outcome.Unknown, r.Outcome);
        Assert.Equal(42, t.sid);
        Assert.Equal(7, t.pir);
        Assert.Equal(1, t.deadChecks);
    }

    /// <summary>
    /// Воскресшая раздача возвращается сразу: обнулённые числа заменяются
    /// живыми, счётчик нулей сбрасывается.
    /// </summary>
    [Fact]
    public void Обнулённая_раздача_воскресает_первым_же_ответом()
    {
        var t = Раздача(sid: 0, pir: 0, deadChecks: 9);

        var r = SweepDecision.Apply(t, seeders: 4, leechers: 2, deadThreshold: 5);

        Assert.Equal(SweepDecision.Outcome.Alive, r.Outcome);
        Assert.True(r.Revived);
        Assert.Equal(4, t.sid);
        Assert.Equal(0, t.deadChecks);
    }
}
