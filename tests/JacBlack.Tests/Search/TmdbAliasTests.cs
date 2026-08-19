using JacBlack.Infrastructure.Metadata;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Другие названия карточки из TMDB.
///
/// Подключены как запасной ход заслона: спрашиваются только при бедной выдаче
/// и только когда известен код IMDB. Набор привязан к карточке кодом, а не
/// подобран по похожести строк, поэтому ошибиться им трудно — но запрос стоит
/// времени, и дёргать его на каждый поиск нельзя.
///
/// Тесты проверяют поведение БЕЗ сети: без ключа и без кода служба обязана
/// молча вернуть пустоту, а не падать и не задерживать поиск. Работу с живым
/// TMDB проверяем на сервере, где ключ задан.
/// </summary>
public class TmdbAliasTests
{
    [Fact]
    public void Без_кода_ничего_не_спрашивается()
    {
        Assert.Null(TmdbAlternativeTitles.ByImdb(null));
        Assert.Null(TmdbAlternativeTitles.ByImdb(""));
        Assert.Null(TmdbAlternativeTitles.ByImdb("   "));
    }

    [Fact]
    public void Неизвестный_код_не_роняет_поиск()
    {
        // Вызывающий обязан работать и без названий — здесь важно, что не
        // выбрасывается исключение и поиск продолжается.
        var titles = TmdbAlternativeTitles.ByImdb("tt00000000");

        Assert.True(titles == null || titles.Count >= 0);
    }
}
