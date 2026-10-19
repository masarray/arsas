using System.Xml.Linq;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Regression contract built from the operator's Siemens SIEBCU field evidence.
/// No customer IID, network endpoint, or device state is copied into the repo.
/// </summary>
public sealed class SiemensBcuP0RegressionTests
{
    [Fact]
    public void CanonicalStaticPlanner_UsesBoundedTargetPages_NotGlobalSixtySixRcbQuota()
    {
        var source = File.ReadAllText(Find("Services/NativeIec61850Client.StaticDataSetReporting.cs"));
        Assert.Contains("MaxReportControlCandidates = 64", source, StringComparison.Ordinal);
        Assert.Contains("MaxCandidatePagesPerInvocation = 8", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxReportControlCandidates = 128", source, StringComparison.Ordinal);
        Assert.Contains("ReadDataSetDirectories = true", source, StringComparison.Ordinal);
        Assert.Contains("MaxExactTargets = 64", source, StringComparison.Ordinal);
        Assert.Contains("CallerOwnedRcbReferences = callerOwnedRcbReferences", source, StringComparison.Ordinal);
        Assert.Contains("TryVerifyStaticDataSetMemberOrder(modelDirectory, liveDirectory", source, StringComparison.Ordinal);
        Assert.Contains("PollingPointKeys = Array.Empty<string>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SiemensXSWIQualityBitString_IsGood_NotRawBitsOrBreakerPosition()
    {
        const string raw = "bits(0000, unused=3)";
        Assert.Equal("Good", Iec61850QualityPresentation.FormatObserved(raw));
        Assert.Equal("good", Iec61850QualityPresentation.FormatObserved(raw, "Validity"));

        var source = File.ReadAllText(Find("MainWindow.IedBrowser.cs"));
        Assert.Contains("EndsWith(\".q\", StringComparison.Ordinal)", source,
            StringComparison.Ordinal);
        Assert.Contains("Iec61850QualityPresentation.FormatObserved(live.Value, row.EvidenceField)", source,
            StringComparison.Ordinal);
        Assert.Contains("display = \"Not decoded\"", source, StringComparison.Ordinal);
        Assert.Contains("IedBrowserDoublePointPresentation.Format(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedWorkbench_DoesNotForceTabContentToEntireStarRow()
    {
        var source = File.ReadAllText(Find("MainWindow.PersistentWorkbench.cs"));
        Assert.Contains("legacyExplorerLayout.MinHeight = 0;", source, StringComparison.Ordinal);
        Assert.Contains("MainTabs.VerticalContentAlignment = VerticalAlignment.Stretch;", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("legacyExplorerLayout.MinHeight = viewport", source,
            StringComparison.Ordinal);
        var xaml = XDocument.Load(Find("MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var grid = Assert.Single(xaml.Descendants(ns + "DataGrid"),
            x => (string?)x.Attribute("Grid.Row") == "2" &&
                 ((string?)x.Attribute("ItemsSource"))?.Contains("SelectedDevice.Points",
                     StringComparison.Ordinal) == true);
        Assert.Equal("{Binding SelectedDevice.Points}", (string?)grid.Attribute("ItemsSource"));
    }

    [Fact]
    public void WindowsInstaller_SuppliesDedicatedRealShortcutIcon()
    {
        var installer = File.ReadAllText(Find("installer/ArIED61850.iss"));
        Assert.Contains(@"Source: ""..\Assets\app-icon.ico""; DestDir: ""{app}\Assets""", installer,
            StringComparison.Ordinal);
        Assert.Contains(@"IconFilename: ""{app}\Assets\app-icon.ico""", installer,
            StringComparison.Ordinal);
        Assert.Contains(@"AppUserModelID: ""masarray.ARSAS.IEC61850""", installer,
            StringComparison.Ordinal);
        Assert.Equal(2, installer.Split(@"AppUserModelID: ""masarray.ARSAS.IEC61850""",
            StringSplitOptions.None).Length - 1);
        var processIdentity = File.ReadAllText(Find("App.WindowsIdentity.cs"));
        Assert.Contains(@"""masarray.ARSAS.IEC61850""", processIdentity,
            StringComparison.Ordinal);
        Assert.True(new FileInfo(Find("Assets/app-icon.ico")).Length > 1000);
        var project = File.ReadAllText(Find("ArIED61850Tester.csproj"));
        Assert.Contains(@"<ApplicationIcon>Assets\app-icon.ico</ApplicationIcon>", project,
            StringComparison.Ordinal);
    }

    private static string Find(string relative)
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null)
        {
            var path = Path.Combine(d.FullName, relative);
            if (File.Exists(path)) return path;
            d = d.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
