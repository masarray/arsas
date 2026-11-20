using System.Text.RegularExpressions;
using Xunit;

namespace ARSAS.Tests;

public sealed class ReleaseCandidateInstallerIsolationTests
{
    [Fact]
    public void RcChannel_HasDistinctIdentityAndNoStableAutoPromotion()
    {
        var version = File.ReadAllText(FromRoot("VERSION")).Trim();
        if (!Regex.IsMatch(version, @"-rc\.\d+$", RegexOptions.CultureInvariant))
            return; // Stable has no RC-only side effects.

        var installer = File.ReadAllText(FromRoot("installer/ArIED61850.iss"));
        var builder = File.ReadAllText(FromRoot("scripts/build-windows-installer.ps1"));
        var package = File.ReadAllText(FromRoot("ArIED61850Tester.csproj"));
        var properties = File.ReadAllText(FromRoot("Directory.Build.props"));
        var publicRelease = File.ReadAllText(FromRoot(".release/windows.json"));

        Assert.Contains("<Version>" + version + "</Version>", package);
        Assert.Contains("<Version>" + version + "</Version>", properties);
        Assert.Contains("<InformationalVersion>" + version + "</InformationalVersion>", properties);
        Assert.Contains("#ifdef ArsasRcBuild", installer);
        Assert.Contains("/DArsasRcBuild=1", builder);
        Assert.Contains("Unrecognized prerelease channel", builder);

        var ids = Regex.Matches(installer, @"AppId=\{\{(?<id>[0-9A-F-]{36})\}")
            .Cast<Match>()
            .Select(m => m.Groups["id"].Value)
            .ToArray();
        Assert.Equal(3, ids.Length);
        Assert.Equal(3, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.Contains("#define AppDirectory \"ARSAS-RC\"", installer);
        Assert.Contains("#define AppShortcut \"ARSAS RC\"", installer);
        Assert.Contains("#define AppUserModelId \"masarray.ARSAS.IEC61850.RC\"", installer);
        Assert.Contains("AppUserModelID: \"{#AppUserModelId}\"", installer);
        Assert.DoesNotContain("\"version\": \"" + version + "\"", publicRelease);
        Assert.Contains("\"channel\": \"stable\"", publicRelease);
    }

    private static string FromRoot(string relative)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine(current.FullName, relative);
            if (File.Exists(path)) return path;
            current = current.Parent;
        }
        throw new FileNotFoundException("RC installation contract source not found: " + relative);
    }
}
