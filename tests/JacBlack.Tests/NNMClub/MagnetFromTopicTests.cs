using JacBlack.Infrastructure.Trackers.NNMClub;
using Xunit;

namespace JacBlack.Tests.NNMClub;

/// <summary>
/// Запасной путь к хешу: magnet со страницы темы.
///
/// Хеш мы берём из торрент-файла, но MonoTorrent не умеет BitTorrent v2 и
/// падает на нём с «The hash root is missing» — раздача пропадала совсем.
/// По прежнему замеру 90 штук в сутки, и это не только книги: 10.09.2026 так
/// терялся сериал «Щит и меч» (тема 577855), а на её странице magnet стоял.
/// </summary>
public class MagnetFromTopicTests
{
    [Fact]
    public void Хеш_берётся_со_страницы_темы()
    {
        string html = "<a href=\"magnet:?xt=urn:btih:1C39E29E4304A23541E27163B0FC6DA1CD002261&amp;tr=http://bt.nnm-club.ws\">Скачать</a>";

        Assert.Equal("magnet:?xt=urn:btih:1c39e29e4304a23541e27163b0fc6da1cd002261",
            NNMClubParser.MagnetFromTopicHtml(html));
    }

    /// <summary>
    /// Гостю magnet показывают не на каждой теме. Нет — значит нет: пустой
    /// ответ лучше выдуманного хеша, по которому ничего не скачается.
    /// </summary>
    [Fact]
    public void Страница_без_магнета_даёт_пусто()
    {
        Assert.Null(NNMClubParser.MagnetFromTopicHtml("<div>Для скачивания нужно войти</div>"));
        Assert.Null(NNMClubParser.MagnetFromTopicHtml(""));
        Assert.Null(NNMClubParser.MagnetFromTopicHtml(null));
    }

    /// <summary>
    /// Хеш ровно сорок шестнадцатеричных знаков: обрезок — не хеш.
    /// </summary>
    [Fact]
    public void Обрезанный_хеш_не_принимается()
    {
        Assert.Null(NNMClubParser.MagnetFromTopicHtml("magnet:?xt=urn:btih:1C39E29E43"));
    }

    /// <summary>
    /// Регистр приводим к нижнему — в базе хеши лежат так же, иначе одна и та
    /// же раздача легла бы двумя записями.
    /// </summary>
    [Fact]
    public void Регистр_приводится_к_нижнему()
    {
        string html = "magnet:?xt=urn:btih:ABCDEF0123456789ABCDEF0123456789ABCDEF01";

        Assert.Equal("magnet:?xt=urn:btih:abcdef0123456789abcdef0123456789abcdef01",
            NNMClubParser.MagnetFromTopicHtml(html));
    }
}
