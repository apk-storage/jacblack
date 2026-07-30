using System.IO;
using Xunit;

namespace JacRed.Tests;

static class FixtureLoader
{
    public static string Read(string relativePath)
    {
        Assert.False(Path.IsPathRooted(relativePath), $"Fixture path must be relative: {relativePath}");
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath);
        Assert.True(File.Exists(path), $"Fixture missing: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Каталог снимков в ИСХОДНИКАХ, а не в сборке. Нужен там, где тест не читает,
    /// а пишет — эталон должен попасть в репозиторий, а не в bin, откуда его смоет
    /// первой же пересборкой.
    /// </summary>
    public static string FixturesRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JacRed.Tests.csproj")))
                dir = dir.Parent;

            Assert.True(dir != null, "не нашёлся каталог тестов от " + AppContext.BaseDirectory);
            return Path.Combine(dir.FullName, "Fixtures");
        }
    }
}
