using System;
using System.Collections.Generic;
using System.Linq;
using JacBlack.Application.Search;
using JacBlack.Models.Details;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Склейка копий одного файла, найденных на РАЗНЫХ трекерах.
///
/// Инфохеш — отпечаток самого файла: совпал значит файл тот же и качаться
/// будет один. Замер 15.08.2026 по «Дому дракона»: на сайте 289 строк на
/// 149 уникальных файлов, то есть 140 строк — повторы.
/// </summary>
public class MergeAcrossTrackersTests
{
    const string HashA = "78bcf88ca2dca689d8e0ac2551b7a38e9a404773";
    const string HashB = "2f1b54bfdb74648eba0b752c6e48f5e87d4c9563";

    static TorrentDetails T(string tracker, string hash, int sid, int pir, string url, string title = "Дом дракона") => new()
    {
        trackerName = tracker,
        magnet = "magnet:?xt=urn:btih:" + hash,
        sid = sid,
        pir = pir,
        url = url,
        title = title,
        updateTime = new DateTime(2026, 8, 1)
    };

    [Fact]
    public void Копии_одного_файла_схлопываются_в_одну_строку()
    {
        var merged = DuplicateFilter.MergeAcrossTrackers(new[]
        {
            T("kinozal", HashA, 54, 54, "https://kinozal.guru/details.php?id=2144503"),
            T("rutor", HashA, 2, 91, "http://rutor.info/torrent/1102805/"),
            T("rutracker", HashA, 2, 91, "https://rutracker.org/forum/viewtopic.php?t=6873874")
        });

        Assert.Single(merged);
        Assert.Equal(3, merged[0].Sources.Count);
    }

    /// <summary>
    /// Главное правило: раздающих берём МАКСИМУМ, а не сумму.
    ///
    /// Сумма была бы враньём — один человек раздаёт один файл и виден всем
    /// трекерам сразу, поэтому сложение посчитало бы его столько раз, на
    /// скольких трекерах лежит копия. На живом примере «Дома дракона» сумма
    /// дала бы 58 там, где раздающих 54.
    /// </summary>
    [Fact]
    public void Раздающих_берём_максимум_а_не_сумму()
    {
        var merged = DuplicateFilter.MergeAcrossTrackers(new[]
        {
            T("kinozal", HashA, 54, 54, "https://kinozal.guru/details.php?id=1"),
            T("rutor", HashA, 2, 91, "http://rutor.info/torrent/2/"),
            T("rutracker", HashA, 2, 91, "https://rutracker.org/forum/viewtopic.php?t=3")
        });

        Assert.Equal(54, merged[0].Item.sid);
        Assert.Equal(91, merged[0].Item.pir);
    }

    [Fact]
    public void Имена_трекеров_собираются_через_запятую()
    {
        var merged = DuplicateFilter.MergeAcrossTrackers(new[]
        {
            T("kinozal", HashA, 5, 5, "https://kinozal.guru/details.php?id=1"),
            T("rutor", HashA, 1, 1, "http://rutor.info/torrent/2/")
        });

        Assert.Contains("kinozal", merged[0].Item.trackerName);
        Assert.Contains("rutor", merged[0].Item.trackerName);
    }

    [Fact]
    public void Разные_файлы_не_склеиваются()
    {
        var merged = DuplicateFilter.MergeAcrossTrackers(new[]
        {
            T("kinozal", HashA, 5, 5, "https://kinozal.guru/details.php?id=1"),
            T("rutor", HashB, 1, 1, "http://rutor.info/torrent/2/")
        });

        Assert.Equal(2, merged.Count);
    }

    /// <summary>
    /// Название выживает одно, а описывают трекеры по-разному: у раздачи
    /// «The Boys» rutor пишет Dolby Vision, а кинозал только HDR10+. Файл
    /// один, и без переноса метки DV-раздача выглядела бы обычной HDR.
    /// </summary>
    [Fact]
    public void Метка_Dolby_Vision_переносится_с_копии()
    {
        var merged = DuplicateFilter.MergeAcrossTrackers(new[]
        {
            T("kinozal", HashA, 9, 9, "https://kinozal.guru/details.php?id=1", "Дом дракона (2026) HDR10+"),
            T("rutor", HashA, 1, 1, "http://rutor.info/torrent/2/", "Дом дракона (2026) Dolby Vision, HDR10")
        });

        Assert.Contains("Dolby Vision", merged[0].Item.title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Запись без magnet сравнивать не с чем — она должна остаться в выдаче,
    /// а не пропасть. У lostfilm magnet добывается отдельным запросом, и
    /// молчаливая потеря таких записей была бы худшим исходом склейки.
    /// </summary>
    [Fact]
    public void Запись_без_инфохеша_остаётся()
    {
        var без = new TorrentDetails { trackerName = "lostfilm", url = "https://www.lostfilm.tv/series/X", title = "X" };

        var merged = DuplicateFilter.MergeAcrossTrackers(new[]
        {
            T("kinozal", HashA, 5, 5, "https://kinozal.guru/details.php?id=1"),
            без
        });

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, m => m.Item.trackerName == "lostfilm");
    }

    [Fact]
    public void Пустой_список_не_роняет()
    {
        Assert.Empty(DuplicateFilter.MergeAcrossTrackers(Array.Empty<TorrentDetails>()));
        Assert.Empty(DuplicateFilter.MergeAcrossTrackers(null));
    }

    /// <summary>
    /// Имя трекера в подписи не повторяется, даже когда у копии поле само
    /// список. Раньше сравнивалась строка целиком: «bitru, kinozal» не
    /// содержит подстроки «kinozal, rutor», и вторая приписывалась вся —
    /// выходило «bitru, kinozal, kinozal, rutor». Замер 10.09.2026 на живом
    /// запросе по «Дюне»: 43 строки из 107 с таким повтором.
    /// </summary>
    [Fact]
    public void Имя_трекера_в_подписи_не_повторяется()
    {
        var merged = DuplicateFilter.MergeAcrossTrackers(new[]
        {
            T("bitru, kinozal", HashA, 5, 5, "https://bitru.org/details.php?id=1"),
            T("kinozal, rutor", HashA, 7, 7, "https://kinozal.guru/details.php?id=2"),
            T("rutor", HashA, 1, 1, "http://rutor.info/torrent/3/")
        });

        Assert.Single(merged);
        Assert.Equal("bitru, kinozal, rutor", merged[0].Item.trackerName);
    }

    /// <summary>
    /// Склейка не трогает записи, которые ей дали: сюда приходят объекты
    /// самой базы, и правка имени оставалась в них навсегда — следующий
    /// поиск складывал уже испорченное поле, и повторы копились.
    /// </summary>
    [Fact]
    public void Склейка_не_меняет_исходные_записи()
    {
        var первая = T("kinozal", HashA, 54, 54, "https://kinozal.guru/details.php?id=1");
        var вторая = T("rutor", HashA, 2, 91, "http://rutor.info/torrent/2/");

        var merged = DuplicateFilter.MergeAcrossTrackers(new[] { первая, вторая });

        Assert.Equal("kinozal, rutor", merged[0].Item.trackerName);
        Assert.Equal("kinozal", первая.trackerName);
        Assert.Equal(54, первая.sid);
        Assert.Equal("rutor", вторая.trackerName);
    }
}
