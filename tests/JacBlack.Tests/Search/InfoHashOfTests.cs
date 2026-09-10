using JacBlack.Infrastructure.Persistence;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Инфохеш из магнет-ссылки — по нему база узнаёт раздачу, перезалитую под
/// новым номером.
///
/// Замер 10.09.2026: в базе 2 867 пар «тот же трекер, тот же файл, другой
/// адрес» — knaben 1 201, eztv 1 041, kinozal 432. У kinozal одна раздача
/// лежала под `kinozal.tv/details.php?id=1828507` и
/// `kinozal.guru/details.php?id=2017675`: номера разные, файл один.
/// </summary>
public class InfoHashOfTests
{
    [Fact]
    public void Хеш_достаётся_и_приводится_к_нижнему_регистру()
    {
        Assert.Equal("1c39e29e4304a23541e27163b0fc6da1cd002261",
            FileDB.InfoHashOf("magnet:?xt=urn:btih:1C39E29E4304A23541E27163B0FC6DA1CD002261&dn=Test"));
    }

    [Fact]
    public void Без_магнета_или_хеша_возвращается_пусто()
    {
        Assert.Null(FileDB.InfoHashOf(null));
        Assert.Null(FileDB.InfoHashOf(""));
        Assert.Null(FileDB.InfoHashOf("magnet:?dn=Test&tr=udp://tracker"));
    }

    /// <summary>
    /// Обрезок хешем не считается: сорок знаков или ничего. Иначе две разные
    /// раздачи схлопнулись бы в одну по общему началу.
    /// </summary>
    [Fact]
    public void Короткий_хеш_не_принимается()
    {
        Assert.Null(FileDB.InfoHashOf("magnet:?xt=urn:btih:1C39E29E4304"));
    }
}
