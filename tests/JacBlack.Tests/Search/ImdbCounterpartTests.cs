using JacBlack.Application.Search;
using JacBlack.Infrastructure.Persistence;
using Xunit;

namespace JacBlack.Tests.Search;

/// <summary>
/// Перевод запроса между языками.
///
/// Индекс базы ищет подстроку по склейке «название : оригинальное название».
/// У русских трекеров там оба языка, поэтому они находятся на любом. А у yts,
/// eztv и piratebay оба поля английские — запрос «Веном» не совпадёт с ними
/// физически. Замер 31.07.2026: «Веном» — 196 раздач и ни одной от yts,
/// «Venom» — 213, из них 35 от yts.
///
/// Лампа кода IMDB не присылает вовсе (ноль из тридцати двух содержательных
/// запросов в журнале), только название — то русское, то оригинальное.
/// Значит перевод должен делать сам поиск.
/// </summary>
public class ImdbCounterpartTests
{
    [Fact]
    public void Русский_запрос_переводится_в_оригинальный()
    {
        ImdbIndex.Remember("tt16366836", "Веном: Последний танец", "Venom: The Last Dance", 2024);

        var counterparts = ImdbIndex.Counterparts("Веном: Последний танец");

        Assert.Contains("Venom: The Last Dance", counterparts);
    }

    [Fact]
    public void Перевод_работает_в_обе_стороны()
    {
        ImdbIndex.Remember("tt1160419", "Дюна", "Dune", 2021);

        Assert.Contains("Dune", ImdbIndex.Counterparts("Дюна"));
        Assert.Contains("Дюна", ImdbIndex.Counterparts("Dune"));
    }

    [Fact]
    public void Знаки_и_регистр_в_запросе_не_мешают()
    {
        ImdbIndex.Remember("tt0816692", "Интерстеллар", "Interstellar", 2014);

        Assert.Contains("Interstellar", ImdbIndex.Counterparts("интерстеллар"));
    }

    [Fact]
    public void Само_себя_переводом_не_считается()
    {
        // Так приходят записи yts: оба поля английские. Возвращать «Venom»
        // в ответ на «Venom» бессмысленно — поиск и так его найдёт.
        ImdbIndex.Remember("tt9999801", "Venom Coast", "Venom Coast", 2021);

        Assert.DoesNotContain("Venom Coast", ImdbIndex.Counterparts("Venom Coast"));
    }

    [Fact]
    public void Копятся_все_написания_а_не_только_первое()
    {
        // Код принёс источник с одним языком, русское написание пришло позже
        // от другого трекера. Раньше побеждал первый, и русского в словаре
        // не оказывалось вовсе — проверено на рабочей базе: записей со словом
        // «Веном» было ноль, потому что первым пришёл yts.
        ImdbIndex.Remember("tt9999802", "Venom: The Last Dance", "Venom: The Last Dance", 2024);
        ImdbIndex.Remember("tt9999802", "Веном: Последний танец", "Venom: The Last Dance", 2024);

        Assert.Contains("Веном: Последний танец", ImdbIndex.Counterparts("Venom: The Last Dance"));
    }

    [Fact]
    public void Неизвестное_название_перевода_не_имеет()
    {
        Assert.Empty(ImdbIndex.Counterparts("такогофильманет"));
        Assert.Empty(ImdbIndex.Counterparts(""));
        Assert.Empty(ImdbIndex.Counterparts(null));
    }

    [Fact]
    public void Поиск_подставляет_перевод_когда_клиент_его_не_прислал()
    {
        ImdbIndex.Remember("tt9999803", "Терминатор", "The Terminator", 1984);

        string resolved = JackettSearchService.ResolveCounterpartTitle("Терминатор", null, null);

        Assert.Equal("The Terminator", resolved);
    }

    [Fact]
    public void Присланное_клиентом_оригинальное_название_не_подменяется()
    {
        // Лампа иногда присылает оба поля сама. Её выбор точнее нашего словаря
        // — она знает карточку, а мы только совпадение названий.
        ImdbIndex.Remember("tt9999804", "Чужой", "Alien", 1979);

        string resolved = JackettSearchService.ResolveCounterpartTitle("Чужой", null, "Alien: Romulus");

        Assert.Equal("Alien: Romulus", resolved);
    }
}
