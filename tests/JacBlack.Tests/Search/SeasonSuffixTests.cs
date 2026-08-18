using System.Collections.Generic;
using JacBlack.Infrastructure.Indexers;
using JacBlack.Models.Api;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Карточка сериала приходит из Лампы вместе с номером сезона — «Tsue to
/// Tsurugi no Wistoria S2», — а трекеры хранят имя СЕРИАЛА, без номера.
/// Строгое равенство названий выбрасывало из-за этого почти всю выдачу:
/// по запросу с «S2» оставался один animetosho (он берёт имя прямо из имени
/// файла релиза, где номер есть), а nyaa, aniliberty, animelayer, rutor и
/// прочие выпадали. Замер на живой базе: 4 раздачи против 38 без номера.
///
/// Поэтому название карточки сравнивается ещё и без хвоста сезона. У самой
/// раздачи хвост не срезается: там номер — часть настоящего имени.
/// </summary>
public class SeasonSuffixTests
{
    static Result Раздача(string имя, int year = 2024) => new()
    {
        Title = имя + " 1080p",
        info = new TorrentInfo
        {
            name = имя,
            originalname = имя,
            relased = year,
            types = new[] { "anime" },
        }
    };

    static IndexerSearchRequest Карточка(string название) => new()
    {
        Title = название,
        TitleOriginal = название,
        Year = 2024,
        IsSerial = 2,
    };

    [Theory]
    [InlineData("Tsue to Tsurugi no Wistoria S2")]
    [InlineData("Tsue to Tsurugi no Wistoria Season 2")]
    [InlineData("Tsue to Tsurugi no Wistoria 2nd Season")]
    public void Сериал_без_номера_сезона_попадает_в_карточку_с_номером(string карточка)
    {
        var выдача = new List<Result> { Раздача("Tsue to Tsurugi no Wistoria") };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка(карточка), originalGiven: true, titleGiven: true);

        Assert.Single(got);
    }

    [Fact]
    public void Раздача_с_тем_же_номером_тоже_остаётся()
    {
        // Прежний путь никуда не делся: точное совпадение работает как работало.
        var выдача = new List<Result> { Раздача("Tsue to Tsurugi no Wistoria S2") };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка("Tsue to Tsurugi no Wistoria S2"), originalGiven: true, titleGiven: true);

        Assert.Single(got);
    }

    [Fact]
    public void Чужой_сериал_похожим_названием_не_проходит()
    {
        // Срезаем номер, а не строгость: другое название остаётся другим.
        var выдача = new List<Result> { Раздача("Tsue to Tsurugi no Wistoria Gaiden") };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка("Tsue to Tsurugi no Wistoria S2"), originalGiven: true, titleGiven: true);

        Assert.Empty(got);
    }

    [Fact]
    public void Карточка_без_номера_ведёт_себя_как_прежде()
    {
        // Хвоста нет — срезать нечего, и лишнего сравнения не делается.
        var выдача = new List<Result>
        {
            Раздача("Tsue to Tsurugi no Wistoria"),
            Раздача("Совсем другое аниме"),
        };

        var got = IndexerSearchHelper.FilterByCardTitle(
            выдача, Карточка("Tsue to Tsurugi no Wistoria"), originalGiven: true, titleGiven: true);

        Assert.Single(got);
    }
}
