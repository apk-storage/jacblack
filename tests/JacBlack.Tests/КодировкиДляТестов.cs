using System.Runtime.CompilerServices;
using System.Text;

namespace JacBlack.Tests;

/// <summary>
/// Включает однобайтовые кодировки до первого теста.
///
/// В .NET кодировки вроде windows-1251 не доступны сами по себе — их даёт
/// `CodePagesEncodingProvider`, и приложение включает его в `Program.cs`.
/// Тесты `Program.cs` не запускают, поэтому любой код, который зовёт
/// `Encoding.GetEncoding(1251)` из статического конструктора, в тестах падал
/// с «No data is available for encoding 1251».
///
/// Держалось это на везении: пока тесты Kinozal шли после чего-то, что уже
/// тронуло провайдер, они проходили. 08.10.2026 добавление двух тестов RuDub
/// поменяло порядок — и пять тестов Kinozal упали разом, хотя их кода никто
/// не касался. Ошибка была не в них и не в RuDub, а в том, что окружение
/// теста отличалось от боевого.
/// </summary>
internal static class КодировкиДляТестов
{
    [ModuleInitializer]
    internal static void Включить()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
