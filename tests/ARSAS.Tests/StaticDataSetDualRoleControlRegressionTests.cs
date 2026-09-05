using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class StaticDataSetDualRoleControlRegressionTests
{
    [Fact]
    public void PositionControlAndStatusRemainSeparateRuntimeFacets()
    {
        var control = new SignalDefinition
        {
            Name = "Pos",
            ObjectReference = "AA1E1F06R4Q0/CSWI1.Pos",
            DisplayReference = "AA1E1F06R4Q0/CSWI1.Pos",
            FunctionalConstraint = "CO",
            DataType = "DPC",
            Category = "Control",
            DataSetReference = "AA1E1F06R4Application/LLN0.Digital",
            IsControlSignal = true,
            ControlCdc = "DPC"
        };
        var status = new SignalDefinition
        {
            Name = "Pos",
            ObjectReference = "AA1E1F06R4Q0/CSWI1.Pos.stVal",
            DisplayReference = "AA1E1F06R4Q0/CSWI1.Pos",
            FunctionalConstraint = "ST",
            DataType = "Dbpos",
            Category = "Position",
            DataSetReference = "AA1E1F06R4Application/LLN0.Digital"
        };

        Assert.True(control.IsValidControlObject);
        Assert.True(control.IsPositionControl);
        Assert.False(control.CanPublishAsSignal);
        Assert.True(status.CanPublishAsSignal);
        Assert.True(status.CanPublishToRuntime);
        Assert.False(status.IsControlSignal);
        Assert.False(SignalDefinition.IsControlObjectReference("AA1E1F06R4Q0/CSWI1.Pos.Oper"));
        Assert.False(SignalDefinition.IsControlObjectReference("AA1E1F06R4Q0/CSWI1.Pos.SBOw"));
        Assert.False(SignalDefinition.IsControlObjectReference("AA1E1F06R4Q0/CSWI1.Pos.ctlVal"));
        Assert.False(SignalDefinition.IsControlObjectReference("AA1E1F06R4Q0/CSWI1.Pos.Cancel"));
    }

    [Theory]
    [InlineData("SPC", "IEDCTRL/GGIO1.Enable", "boolean")]
    [InlineData("DPC", "IEDQ0/CSWI1.Pos", "position")]
    [InlineData("INC", "IEDCTRL/ATCC1.TapCmd", "regulating")]
    [InlineData("ISC", "IEDCTRL/ATCC1.StepCmd", "regulating")]
    [InlineData("APC", "IEDCTRL/AVCO1.VRef", "setpoint")]
    [InlineData("BAC", "IEDCTRL/GAPC1.AnCtl", "setpoint")]
    [InlineData("BSC", "IEDCTRL/ATCC1.TapPos", "setpoint")]
    public void ExistingCommandSurface_CoversStandardControlFamilies(
        string cdc,
        string reference,
        string expectedKind)
    {
        var signal = new SignalDefinition
        {
            Name = reference[(reference.LastIndexOf('.') + 1)..],
            ObjectReference = reference,
            DisplayReference = reference,
            FunctionalConstraint = "CO",
            DataType = cdc,
            Category = "Control",
            IsControlSignal = true,
            ControlCdc = cdc
        };

        Assert.True(signal.IsValidControlObject);
        Assert.False(signal.CanPublishAsSignal);
        Assert.False(signal.IsGenericControl);

        switch (expectedKind)
        {
            case "boolean":
                Assert.True(signal.IsBooleanControl);
                break;
            case "position":
                Assert.True(signal.IsPositionControl);
                break;
            case "regulating":
                Assert.True(signal.IsRaiseLowerControl);
                break;
            case "setpoint":
                Assert.True(signal.IsSetPointControl);
                break;
        }
    }

    [Fact]
    public void StaticControlProjection_UsesExactCdcAndDataObjectAuthority_NotObjectNameGuessing()
    {
        var source = File.ReadAllText(FindRepoFile(
            "Services/Iec61850StaticControlStatusProjectionService.cs"));

        foreach (var cdc in new[] { "SPC", "DPC", "INC", "ISC", "APC", "BAC", "BSC", "ENC" })
            Assert.Contains($"\"{cdc}\"", source, StringComparison.Ordinal);

        Assert.Contains("FirstNonEmpty(descriptor.Cdc, membership.Cdc)", source, StringComparison.Ordinal);
        Assert.Contains("descriptor.DataObjectReference", source, StringComparison.Ordinal);
        Assert.Contains("CreateControlCompanion", source, StringComparison.Ordinal);
        Assert.Contains("ControlCdc = cdc", source, StringComparison.Ordinal);
        Assert.Contains("FunctionalConstraint = \"CO\"", source, StringComparison.Ordinal);
        Assert.Contains("ControlStatusReference = exactFeedbackReference", source, StringComparison.Ordinal);
        Assert.Contains("Actual command actions remain disabled until live ctlModel inspection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PositionLogicalNodeClasses", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExactPositionMemberReference", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartsWith(memberReference", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticControlProjection_RetainsControlWhenScalarFeedbackIsUnresolved()
    {
        var projection = File.ReadAllText(FindRepoFile(
            "Services/Iec61850StaticControlStatusProjectionService.cs"));
        var authority = File.ReadAllText(FindRepoFile(
            "Services/Iec61850StaticDataSetAuthoritySelection.cs"));

        Assert.Contains("if (control is null)", projection, StringComparison.Ordinal);
        Assert.Contains("device.Signals.Add(control)", projection, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(exactFeedbackReference))", projection, StringComparison.Ordinal);

        var controlSelection = authority.IndexOf(
            ".Where(signal => signal.IsControlSignal && signal.IsValidControlObject)",
            StringComparison.Ordinal);
        var runtimeCandidates = authority.IndexOf(
            "var candidates = signals",
            StringComparison.Ordinal);
        var noRuntimeCandidate = authority.IndexOf(
            "if (candidates.Length == 0)",
            StringComparison.Ordinal);

        Assert.True(controlSelection >= 0);
        Assert.True(runtimeCandidates > controlSelection);
        Assert.True(noRuntimeCandidate > controlSelection);
        Assert.Contains("selected.Add(control)", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceLeavesNeverBecomeProcessSignalsOrCommandTargets()
    {
        var projection = File.ReadAllText(FindRepoFile(
            "Services/Iec61850StaticControlStatusProjectionService.cs"));
        var runtime = File.ReadAllText(FindRepoFile(
            "Services/Iec61850MonitorRuntime.cs"));

        foreach (var leaf in new[] { "ctlModel", "ctlVal", "SBO", "SBOw", "Oper", "Cancel", "origin", "Check", "Test" })
            Assert.Contains($"\"{leaf}\"", projection, StringComparison.Ordinal);

        Assert.Contains("ControlServicePathSegments.Contains(segment)", projection, StringComparison.Ordinal);
        Assert.Contains(
            ".Where(signal => signal.IsSelected && signal.CanPublishToRuntime)",
            runtime,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "signal.IsSelected && (signal.CanPublishToRuntime || signal.IsControlSignal)",
            runtime,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SharedStaticWorkflow_MaterializesControlsBeforeAuthoritySelection()
    {
        var registration = File.ReadAllText(FindRepoFile("MainWindow.DataSetSignalInventory.cs"));
        var shared = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));

        Assert.Contains(
            "Iec61850StaticControlStatusProjectionService.EnsureProjections(device)",
            registration,
            StringComparison.Ordinal);
        Assert.Contains(
            "merge.AddedSignals.Concat(controlStatusProjection.AddedSignals)",
            registration,
            StringComparison.Ordinal);
        Assert.Contains("AddedControlCount", registration, StringComparison.Ordinal);
        Assert.Contains("AddedRuntimeFeedbackCount", registration, StringComparison.Ordinal);
        Assert.Contains("Commands remain gated by live ctlModel", registration, StringComparison.Ordinal);

        var registerIndex = shared.IndexOf("RegisterRecoveredDataSetSignals(device, merge)", StringComparison.Ordinal);
        var authorityIndex = shared.IndexOf("Iec61850StaticDataSetAuthoritySelection.Build(device)", StringComparison.Ordinal);
        Assert.True(registerIndex >= 0 && authorityIndex > registerIndex);
    }

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

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
