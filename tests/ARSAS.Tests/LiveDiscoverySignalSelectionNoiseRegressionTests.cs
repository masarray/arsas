using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class LiveDiscoverySignalSelectionNoiseRegressionTests
{
    [Theory]
    [InlineData("AA1C1F13R4ADD/GAPC1.Mod.stVal", "GAPC1", "ST", "Boolean", "Status")]
    [InlineData("AA1C1F13R4ADD/GAPC1.Beh.stVal", "GAPC1", "ST", "Boolean", "Status")]
    [InlineData("AA1C1F13R4ADD/GAPC1.Health.stVal", "GAPC1", "ST", "Boolean", "Status")]
    [InlineData("AA1C1F13R4ADD/GAPC1.NamPlt.d", "GAPC1", "DC", "VisString255", "Metadata")]
    [InlineData("AA1C1F13R4ADD/GGIO1.LocOpnCMDsta.subVal", "GGIO1", "SV", "Boolean", "Status")]
    [InlineData("AA1C1F13R4ADD/GGIO1.LocOpnCMDsta.subQ", "GGIO1", "SV", "Quality", "Quality")]
    [InlineData("AA1C1F13R4ADD/GGIO1.LocOpnCMDsta.blkEna", "GGIO1", "BL", "Boolean", "Status")]
    [InlineData("AA1C1F13R4ADD/GGIO1.LocOpnCMDsta.d.stVal", "GGIO1", "ST", "Boolean", "Status")]
    [InlineData("AA1C1F13R4Application/LLN0.q.stVal", "LLN0", "ST", "Boolean", "Status")]
    public void EngineeringAndProtocolLeaves_AreRejectedFromLiveSignalSelection(
        string reference,
        string logicalNode,
        string fc,
        string dataType,
        string category)
    {
        var signal = NewSignal(reference, logicalNode, fc, dataType, category);

        Assert.True(LiveDiscoverySignalSelectionPolicy.IsProtocolOrEngineeringNoise(reference));
        Assert.False(LiveDiscoverySignalSelectionPolicy.IsVisible(signal));
    }

    [Theory]
    [InlineData("AA1C1F13R4ADD/GGIO1.Mod.Oper.origin.orCat")]
    [InlineData("AA1C1F13R4ADD/GGIO1.Mod.Oper.origin.orIdent")]
    [InlineData("AA1C1F13R4ADD/GGIO1.Mod.Oper.ctlVal")]
    [InlineData("AA1C1F13R4ADD/GGIO1.Mod.Oper.ctlNum")]
    [InlineData("AA1C1F13R4ADD/GGIO1.Mod.Oper.Check")]
    [InlineData("AA1C1F13R4ADD/GGIO1.Mod.ctlModel")]
    public void ControlServiceLeaves_AreRejectedFromLiveSignalSelection(string reference)
    {
        var signal = NewSignal(reference, "GGIO1", "CO", "Struct", "Control");
        signal.IsControlSignal = true;
        signal.ControlCdc = "SPC";

        Assert.True(LiveDiscoverySignalSelectionPolicy.IsProtocolOrEngineeringNoise(reference));
        Assert.False(LiveDiscoverySignalSelectionPolicy.IsVisible(signal));
    }

    [Theory]
    [InlineData("IEDLD/XCBR1.Pos.stVal", "XCBR1", "ST", "Enum", "Position")]
    [InlineData("IEDLD/MMXU1.A.phsA.cVal.mag.f", "MMXU1", "MX", "Float32", "Measurement")]
    [InlineData("IEDLD/PTRC1.Tr.general", "PTRC1", "ST", "Boolean", "Protection")]
    [InlineData("IEDLD/GGIO1.Ind15.stVal", "GGIO1", "ST", "Boolean", "Status")]
    public void RealOperatorPoints_RemainVisible(
        string reference,
        string logicalNode,
        string fc,
        string dataType,
        string category)
    {
        var signal = NewSignal(reference, logicalNode, fc, dataType, category);

        Assert.False(LiveDiscoverySignalSelectionPolicy.IsProtocolOrEngineeringNoise(reference));
        Assert.True(LiveDiscoverySignalSelectionPolicy.IsVisible(signal));
    }

    [Fact]
    public void RealPositionControl_RemainsVisible()
    {
        var signal = NewSignal("IEDLD/CSWI1.Pos", "CSWI1", "CO", "Struct", "Control");
        signal.IsControlSignal = true;
        signal.ControlCdc = "DPC";

        Assert.True(LiveDiscoverySignalSelectionPolicy.IsVisible(signal));
    }

    [Fact]
    public void StaticDataSetObjectLevelMember_RemainsVisibleEvenWithoutRuntimeLeaf()
    {
        var signal = NewSignal(
            "AA1C1F13R4ADD/GGIO6.CBOpnd",
            "GGIO6",
            "ST",
            "Boolean",
            "DataSet");
        signal.DataSetReference = "AA1C1F13R4Application/LLN0$Digital";
        signal.DisplayReference = signal.ObjectReference;

        Assert.False(SasOperationalSignalPolicy.IsVisible(signal));
        Assert.True(LiveDiscoverySignalSelectionPolicy.IsVisible(signal));
        Assert.DoesNotContain(".stVal", signal.DisplayReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaticDataSetAuthority_WinsEvenIfConfiguredMemberLooksLikeEngineeringState()
    {
        var signal = NewSignal(
            "IEDLD/LLN0.Beh",
            "LLN0",
            "ST",
            "Enum",
            "DataSet");
        signal.DataSetReference = "IEDLD/LLN0$Configured";

        Assert.True(LiveDiscoverySignalSelectionPolicy.IsVisible(signal));
    }

    [Fact]
    public void SignalSelectionWizard_InstallsOperationalFilterAfterConstructorFilters()
    {
        var source = File.ReadAllText(FindRepoFile("SignalSelectionWizardWindow.LiveDiscoveryOperationalFilter.cs"));

        Assert.Contains("FrameworkElement.LoadedEvent", source, StringComparison.Ordinal);
        Assert.Contains("_signalSelectionBaseFilter = SignalsView.Filter", source, StringComparison.Ordinal);
        Assert.Contains("LiveDiscoverySignalSelectionPolicy.IsVisible(signal)", source, StringComparison.Ordinal);
        Assert.Contains("SignalsView.Filter = _signalSelectionOperationalFilter", source, StringComparison.Ordinal);
        Assert.Contains("SignalsView.Filter = _signalSelectionBaseFilter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveAt(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Signals.Clear", source, StringComparison.Ordinal);
    }

    private static SignalDefinition NewSignal(
        string reference,
        string logicalNode,
        string fc,
        string dataType,
        string category)
        => new()
        {
            Name = logicalNode,
            ObjectReference = reference,
            FunctionalConstraint = fc,
            DataType = dataType,
            Category = category,
            Source = "ARIEC61850 live discovery",
            ProbeStatus = "Readable",
            Value = "0",
            Quality = "Good"
        };

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
