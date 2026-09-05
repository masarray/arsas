using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

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
            FunctionalConstraint = "ST",
            DataType = "Dbpos",
            Category = "Control",
            DataSetReference = "AA1E1F06R4Application/LLN0.Digital",
            IsControlSignal = true
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
    }

    [Fact]
    public void StaticPositionProjection_RequiresExactAriecStAuthority()
    {
        var source = File.ReadAllText(FindRepoFile(
            "Services/Iec61850StaticControlStatusProjectionService.cs"));

        Assert.Contains("descriptor.FunctionalConstraint.Equals(\"ST\"", source, StringComparison.Ordinal);
        Assert.Contains("descriptor.PrimaryValueReference", source, StringComparison.Ordinal);
        Assert.Contains("primary.EndsWith(\".stval\"", source, StringComparison.Ordinal);
        Assert.Contains("\"CSWI\", \"XCBR\", \"XSWI\"", source, StringComparison.Ordinal);
        Assert.Contains("signal.IsControlSignal && signal.IsValidControlObject", source, StringComparison.Ordinal);
        Assert.Contains("LiteralEquals(signal.DisplayReference, memberReference)", source, StringComparison.Ordinal);
        Assert.Contains("LiteralEquals(signal.ObjectReference, memberReference)", source, StringComparison.Ordinal);
        Assert.Contains("ObjectReference = primaryValueReference", source, StringComparison.Ordinal);
        Assert.Contains("FunctionalConstraint = \"ST\"", source, StringComparison.Ordinal);
        Assert.Contains("Category = \"Position\"", source, StringComparison.Ordinal);
        Assert.Contains("dual-role control status projection", source, StringComparison.Ordinal);
        Assert.Contains("No prefix/fuzzy matching", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartsWith(memberReference", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticAuthority_SelectsExactControlCompanionButRuntimeStillRejectsRawControlObjects()
    {
        var authority = File.ReadAllText(FindRepoFile(
            "Services/Iec61850StaticDataSetAuthoritySelection.cs"));
        var runtime = File.ReadAllText(FindRepoFile(
            "Services/Iec61850MonitorRuntime.cs"));

        Assert.Contains(
            ".Where(signal => !signal.IsControlSignal && signal.CanPublishToRuntime)",
            authority,
            StringComparison.Ordinal);
        Assert.Contains(
            ".Where(signal => signal.IsControlSignal && signal.IsValidControlObject)",
            authority,
            StringComparison.Ordinal);
        Assert.Contains(
            "LiteralEquals(signal.DataSetReference, membership.DataSetReference)",
            authority,
            StringComparison.Ordinal);
        Assert.Contains(
            "LiteralEquals(signal.DisplayReference, memberReference)",
            authority,
            StringComparison.Ordinal);
        Assert.Contains("selected.Add(control)", authority, StringComparison.Ordinal);

        // The raw control DO is selected only so ctlModel inspection / Command Panel can use
        // it. Monitoring still admits only the separate non-control ST status projection.
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
    public void SharedStaticWorkflow_MaterializesDualRoleStatusBeforeAuthoritySelection()
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
        Assert.Contains("control service leaves remain excluded", registration, StringComparison.OrdinalIgnoreCase);

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
