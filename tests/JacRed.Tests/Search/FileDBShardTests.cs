using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using JacRed.Infrastructure.Persistence;
using JacRed.Models.Details;
using Xunit;

namespace JacRed.Tests.Search;

/// <summary>
/// Работа с шардом базы: открытие, разделение между потоками, отметка об
/// изменении.
///
/// База разложена по файлам-шардам, ключ шарда — «имя:оригинальное имя».
/// Один и тот же фильм приходит с разных трекеров, поэтому параллельные
/// обходы регулярно метят в ОДИН шард. Тесты ниже сторожат именно это место.
/// </summary>
public class FileDBShardTests
{
    /// <summary>
    /// Дату публикации закрепляем. По умолчанию она равна «сейчас», а запись
    /// принимает дату новее имеющейся — то есть два одинаковых по смыслу
    /// вызова подряд различались бы одним лишь моментом создания объекта.
    /// Парсеры дату проставляют сами, все семнадцать: в рабочей базе записей
    /// с пустой датой ноль из 1 226 364 (проверено 30.07.2026).
    /// </summary>
    static readonly DateTime Published = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc);

    static TorrentDetails Torrent(string url, string name, params string[] types) => new TorrentDetails
    {
        url = url,
        trackerName = "rutor",
        title = name,
        name = name,
        originalname = name,
        magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
        types = types.Length > 0 ? types : new[] { "movie" },
        relased = 2024,
        sizeName = "1.5 GB",
        createTime = Published
    };

    [Fact]
    public async Task Параллельное_открытие_даёт_один_экземпляр()
    {
        // Раньше открытие шло через TryGetValue + TryAdd, и два потока,
        // разошедшиеся на этой паре, получали ДВА разных экземпляра одного
        // шарда: победитель попадал в кеш, проигравшему возвращали его
        // собственный. Дальше оба читали один файл, писали каждый в свою копию
        // и по очереди сохраняли — чьи записи легли вторыми, те и оставались.
        string key = FileDB.KeyForTorrent("проба одновременного открытия", "concurrent open probe");
        var opened = new ConcurrentBag<FileDB>();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            var fdb = FileDB.OpenWrite(key);
            opened.Add(fdb);
        })));

        var instances = opened.ToArray();
        Assert.Equal(32, instances.Length);
        Assert.Single(instances.Distinct(ReferenceComparer.Instance));

        foreach (var fdb in instances)
        {
            fdb.savechanges = false;
            fdb.Dispose();
        }
    }

    [Fact]
    public void Сузившийся_набор_типов_помечает_запись_изменённой()
    {
        // Раньше набор типов присваивался БЕЗ отметки об изменении, если он
        // только сузился. Запись менялась в памяти, а на диск не просилась —
        // правка терялась, пока её случайно не выносило соседнее поле.
        const string name = "проба сужения типов";
        string key = FileDB.KeyForTorrent(name, name);
        var fdb = FileDB.OpenWrite(key);

        try
        {
            fdb.AddOrUpdate(Torrent("https://example.org/torrent/1", name, "movie", "serial"));

            fdb.savechanges = false;
            fdb.AddOrUpdate(Torrent("https://example.org/torrent/1", name, "movie"));

            Assert.True(fdb.savechanges, "сужение набора типов осталось непомеченным");
        }
        finally
        {
            fdb.savechanges = false;
            fdb.Dispose();
        }
    }

    [Fact]
    public void Неизменившийся_набор_типов_ничего_не_помечает()
    {
        // Обратная сторона: повторный обход присылает то же самое сотнями тысяч
        // записей, и каждая не должна тянуть за собой перезапись шарда.
        const string name = "проба неизменных типов";
        string key = FileDB.KeyForTorrent(name, name);
        var fdb = FileDB.OpenWrite(key);

        try
        {
            fdb.AddOrUpdate(Torrent("https://example.org/torrent/2", name, "movie"));

            fdb.savechanges = false;
            fdb.AddOrUpdate(Torrent("https://example.org/torrent/2", name, "movie"));

            Assert.False(fdb.savechanges, "повтор того же набора типов зря пометил запись");
        }
        finally
        {
            fdb.savechanges = false;
            fdb.Dispose();
        }
    }

    sealed class ReferenceComparer : System.Collections.Generic.IEqualityComparer<FileDB>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public bool Equals(FileDB x, FileDB y) => ReferenceEquals(x, y);

        public int GetHashCode(FileDB obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
