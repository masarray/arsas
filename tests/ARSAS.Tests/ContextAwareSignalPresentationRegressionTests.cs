using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class ContextAwareSignalPresentationRegressionTests
{
    [Theory]
    [InlineData("A", "IEDLD/MMXU1.A.phsA.cVal.mag.f", "A PhsA")]
    [InlineData("A", "IEDLD/MMXU1.A.phsB.cVal.mag.f", "A PhsB")]
    [InlineData("A", "IEDLD/MMXU1.A.phsC.cVal.mag.f", "A PhsC")]
    [InlineData("PhV", "IEDLD/MMXU1.PhV.phsA.cVal.mag.f", "PhV PhsA")]
    [InlineData("Pos", "IEDLD/XCBR1.Pos.stVal", "XCBR Pos")]
    [InlineData("Pos", "IEDLD/CSWI1.Pos.stVal", "CSWI Pos")]
    public void SharedEngineeringPresentation_IsPhaseAndOwnerAware(
        string signalName,
        string reference,
        string expected)
    {
        Assert.Equal(expected, IoSignalDisplayName.Format(signalName, reference));
    }

    [Fact]
    public void FatPresentation_RecoversPhaseFromDaContext_WhenLookupReferenceIsShorter()
    {
        var point = Point(
            signalName: "A",
            objectReference: "IEDLD/MMXU1.A.cVal.mag.f",
            logicalNode: "MMXU1",
            dataObject: "A",
            dataAttribute: "phsB.cVal.mag.f",
            sourceReference: "IEDLD/MMXU1.A");

        Assert.Equal("A PhsB", IoFatSignalDisplayNameFormatter.Format(point));
    }

    [Theory]
    [InlineData("XCBR1", "XCBR Pos")]
    [InlineData("CSWI1", "CSWI Pos")]
    public void FatPresentation_QualifiesGenericPositionWithOwningLogicalNode(
        string logicalNode,
        string expected)
    {
        var point = Point(
            signalName: "Pos",
            objectReference: $"IEDLD/{logicalNode}.Pos.stVal",
            logicalNode: logicalNode,
            dataObject: "Pos",
            dataAttribute: "stVal",
            sourceReference: $"IEDLD/{logicalNode}.Pos.stVal");

        Assert.Equal(expected, IoFatSignalDisplayNameFormatter.Format(point));
    }

    [Theory]
    [InlineData("A PhsA", "IEDLD/MMXU1.A.phsA.cVal.mag.f", "A PhsA")]
    [InlineData("XCBR Pos", "IEDLD/XCBR1.Pos.stVal", "XCBR Pos")]
    public void SemanticPresentation_DoesNotDuplicateExistingContext(
        string signalName,
        string reference,
        string expected)
    {
        Assert.Equal(expected, IoSignalDisplayName.Format(signalName, reference));
    }

    [Fact]
    public void FatReport_UsesTheSamePointAwareSemanticFormatterAsFatWorkspace()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatV2ReportLayoutEngine.cs"));

        Assert.Contains("IoFatSignalDisplayNameFormatter.Format(point)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IoSignalDisplayName.Format(point.SignalName, point.ReportIecReference)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringAndFat_ConvergeOnTheSameSemanticAuthority()
    {
        var engineering = File.ReadAllText(FindRepoFile("MainWindow.FieldPresentationFix.cs"));
        var fat = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatSignalDisplayNameFormatter.cs"));

        Assert.Contains("IoSignalDisplayName.Format(preferred, reference)", engineering, StringComparison.Ordinal);
        Assert.Contains("IoSignalDisplayName.Format(", fat, StringComparison.Ordinal);
        Assert.Contains("point.SourceIecReference", fat, StringComparison.Ordinal);
        Assert.Contains("point.LogicalNode", fat, StringComparison.Ordinal);
        Assert.Contains("point.DataObject", fat, StringComparison.Ordinal);
        Assert.Contains("point.DataAttribute", fat, StringComparison.Ordinal);
    }

    [Fact]
    public void FatLiveValueBinding_RemainsCanonicalReferenceBased_NotDisplayNameBased()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestLiveBindingService.cs"));

        Assert.Contains(
            "expectedReferences.Contains(NormalizeReference(item.IecReference))",
            source,
            StringComparison.Ordinal);
        Assert.Contains("binding.LivePoint.Value", source, StringComparison.Ordinal);
        Assert.Contains("ExactSignalIdentityMatches", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalName.Equals(", source, StringComparison.Ordinal);
    }

    private static IoTestPointPlan Point(
        string signalName,
        string objectReference,
        string logicalNode,
        string dataObject,
        string dataAttribute,
        string sourceReference)
        => new()
        {
            TestPointId = Guid.NewGuid().ToString("N"),
            IedName = "IED",
            IpAddress = "192.0.2.1",
            SignalName = signalName,
            ObjectReference = objectReference,
            FunctionalConstraint = "ST",
            ExpectedOnText = "ON",
            ExpectedOffText = "OFF",
            LogicalDevice = "IEDLD",
            LogicalNode = logicalNode,
            DataObject = dataObject,
            DataAttribute = dataAttribute,
            SourceIecReference = sourceReference,
            ReportDisplayReference = sourceReference,
            EventLogSearchReference = objectReference
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
