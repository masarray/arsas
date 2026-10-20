using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0FBrowserCardAndFcReadScopeTests
{
    [Theory]
    [InlineData("SV")]
    [InlineData("OR")]
    [InlineData("BL")]
    public void SubstitutionAndOperationStatesRequireExplicitVerifiedRead(string fc)
    {
        Assert.Equal("Not read", IedBrowserReadPlan.UnobservedValue(fc));
        Assert.Contains("explicit Read LN/DO",
            IedBrowserReadPlan.UnobservedExplanation(fc), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RP")]
    [InlineData("BR")]
    public void ControlBlocksRemainOutsideGenericScalarRead(string fc)
    {
        Assert.Equal("Outside read scope", IedBrowserReadPlan.UnobservedValue(fc));
        Assert.Contains("No Read attempt was scheduled",
            IedBrowserReadPlan.UnobservedExplanation(fc), StringComparison.Ordinal);
    }

    [Fact]
    public void ExtendedFcAdmissionIsWiredOnlyFromExplicitUserRead()
    {
        var code = Read("MainWindow.IedBrowser.cs");
        Assert.Contains("includeExplicitStateFc: !automatic", code);
        var plan = Read("Models/IedBrowserReadPlan.cs");
        Assert.Contains("includeExplicitStateFc = false", plan);
        Assert.Contains("includeExplicitStateFc && ExplicitStateFc.Contains", plan);
        Assert.Contains("private static readonly HashSet<string> ExplicitStateFc", plan);
    }

    [Theory]
    [InlineData("ST")]
    [InlineData("MX")]
    [InlineData("CF")]
    public void VerifiedReadScopeRetainsUnobservedState(string fc)
    {
        Assert.Equal("Not read", IedBrowserReadPlan.UnobservedValue(fc));
        Assert.Equal("", IedBrowserReadPlan.UnobservedExplanation(fc));
    }

    [Fact]
    public void BrowserControlFunctionIsNotAccidentallyEnabled()
    {
        Assert.Equal("Control only", IedBrowserReadPlan.UnobservedValue("CO"));
        Assert.Contains("never operates controls",
            IedBrowserReadPlan.UnobservedExplanation("CO"), StringComparison.Ordinal);
    }

    [Fact]
    public void IedCardProgressOccupiesAStableSlotAndNeverShowsBlockingOverlayForRead()
    {
        var xaml = Read("MainWindow.xaml");
        Assert.Contains("<Grid Height=\"5\" Margin=\"6,4,6,0\"", xaml);
        Assert.Contains("x:Name=\"BrowserCardReadProgress\"", xaml);
        Assert.Contains("x:Name=\"BrowserCardReadTrack\"", xaml);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsBrowserReading}\" Value=\"True\">", xaml);
        var trigger = xaml.Split("<DataTrigger Binding=\"{Binding IsBrowserReading}\" Value=\"True\">")[1]
            .Split("</DataTrigger>")[0];
        Assert.Contains("BrowserCardReadProgress", trigger);
        Assert.Contains("BrowserCardReadTrack", trigger);
        Assert.DoesNotContain("BusyOverlay", trigger);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsBusy}\" Value=\"True\">", xaml);
        Assert.Contains("<Setter TargetName=\"BusyOverlay\" Property=\"Visibility\" Value=\"Visible\"/>", xaml);
    }

    private static string Read(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, name);
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }
}
