namespace ARSAS.Tests;

public sealed class SmvPreviewInstallerIsolationTests
{
    [Fact]
    public void PreviewBuildHasIndependentWindowsInstallIdentity()
    {
        var iss = Read("installer/ArIED61850.iss");
        var script = Read("scripts/build-windows-installer.ps1");

        Assert.Contains("#ifdef SmvPreviewBuild", iss, StringComparison.Ordinal);
        Assert.Contains("AppId={{9E3B17A4-38EC-4B2B-B61A-72788FF78B8A}", iss, StringComparison.Ordinal);
        Assert.Contains("AppId={{B68E7E86-4881-40CA-9BE2-5C553F74C72F}", iss, StringComparison.Ordinal);
        Assert.Contains("ARSAS-SMV-Preview", iss, StringComparison.Ordinal);
        Assert.Contains("AppShortcut", iss, StringComparison.Ordinal);
        Assert.Contains("/DSmvPreviewBuild=1", script, StringComparison.Ordinal);
        Assert.Contains("-smvpreview", script, StringComparison.Ordinal);
        Assert.Contains("& $InnoCompiler @arguments", script, StringComparison.Ordinal);
        Assert.Contains("Inno Setup compilation failed", script, StringComparison.Ordinal);
    }

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, relative);
            if (File.Exists(p)) return File.ReadAllText(p);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
