using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Stable release identity must come from the installed executable assembly,
/// and must agree with both app and installer version authorities.
/// </summary>
public sealed class P109StableVersionTitleRegressionTests
{
    [Fact]
    public void NativeMainWindowTitleUsesCompiledAssemblyVersion()
    {
        var main = Read("MainWindow.xaml.cs");
        Assert.Contains("typeof(MainWindow).Assembly.GetName().Version", main, StringComparison.Ordinal);
        Assert.Contains("installedVersion.ToString(3)", main, StringComparison.Ordinal);
        Assert.Contains("ARSAS v{installedVersion.ToString(3)}", main, StringComparison.Ordinal);
        Assert.DoesNotContain("ARSAS v1.6.41 —", main, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestedReleaseVersionMatchesNativeAssemblyAndProjectMetadata()
    {
        const string expected = "1.6.41";
        var props = XDocument.Parse(Read("Directory.Build.props"));
        var csproj = XDocument.Parse(Read("ArIED61850Tester.csproj"));
        Assert.Equal(expected, Read("VERSION").Trim());
        Assert.Equal(expected, props.Descendants("Version").Single().Value);
        Assert.Equal(expected, csproj.Descendants("Version").Single().Value);
        Assert.Equal(expected + ".0", props.Descendants("AssemblyVersion").Single().Value);
        Assert.Equal(expected + ".0", csproj.Descendants("AssemblyVersion").Single().Value);
        Assert.Contains("\"version\": \"1.6.41\"", Read(".release/windows.json"), StringComparison.Ordinal);
    }

    private static string Read(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }
}
