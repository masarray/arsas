using System.IO;
namespace ARSAS.Tests;
public sealed class SmvSmartInstallerUpgradeTests
{
    [Fact]
    public void PreviewRequiresElevation_AndNeverOffersPerUserModeInProgramFiles()
    {
        var script = Read("installer/ArIED61850.iss");
        var begin = script.IndexOf("PrivilegesRequired=admin", StringComparison.Ordinal);
        var end = script.IndexOf("CloseApplications=yes", begin, StringComparison.Ordinal);
        var block = script[begin..end];
        Assert.Contains("#ifndef SmvPreviewBuild", block);
        Assert.Contains("PrivilegesRequiredOverridesAllowed=dialog", block);
        Assert.Contains("PrivilegesRequired=admin", block);
    }

    [Fact]
    public void PreviewSupportsSafeLockedFileRebootRecovery_WithoutKillingUnrelatedApps()
    {
        var script = Read("installer/ArIED61850.iss");
        Assert.Contains("CloseApplications=yes", script);
        Assert.Contains("CloseApplicationsFilter=*.exe,*.dll,*.chm", script);
        Assert.DoesNotContain("CloseApplications=force", script);
        Assert.Contains("Flags: ignoreversion recursesubdirs createallsubdirs restartreplace", script);
        Assert.Contains("function PrepareToInstall(var NeedsRestart: Boolean): String;", script);
        Assert.Contains("SMART_UPGRADE_PREFLIGHT", script);
        Assert.Contains("RegisterExtraCloseApplicationsResources", script);
        Assert.Contains("RegisterExtraCloseApplicationsResource(False, LegacyDll)", script);
    }

    [Fact]
    public void PreviewReleaseMustTestProtectedFolder_InPlaceDllRepairAndUninstall()
    {
        var ci = Read(".github/workflows/smv-direct-inno-preview.yml");
        Assert.Contains("Join-Path $env:ProgramFiles 'ARSAS-SMV-Preview-CI'", ci);
        Assert.DoesNotContain("'/CURRENTUSER'", ci);
        Assert.Contains("$dirArgument = '/DIR=\"' + $target + '\"'", ci);
        Assert.Contains("Silent install did not use requested Program Files directory", ci);
        Assert.Contains("WriteAllBytes($repairPath", ci);
        Assert.Contains("Seeded a legacy Accessibility.dll", ci);
        Assert.Contains("SMART_INNO_UPGRADE_PASS", ci);
        Assert.Contains("Get-FileHash $repairPath -Algorithm SHA256", ci);
        Assert.Contains("Uninstall failed", ci);
    }

    private static string Read(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, relative);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(relative);
    }
}
