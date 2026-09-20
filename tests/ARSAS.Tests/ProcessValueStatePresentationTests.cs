using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class ProcessValueStatePresentationTests
{
    [Theory]
    [InlineData("True", "Boolean", Iec61850ValueStatePresentation.Active)]
    [InlineData("True [1]", "Boolean", Iec61850ValueStatePresentation.Active)]
    [InlineData("ON", "Boolean", Iec61850ValueStatePresentation.Active)]
    [InlineData("Closed [10]", "Dbpos", Iec61850ValueStatePresentation.Active)]
    [InlineData("False", "Boolean", Iec61850ValueStatePresentation.Inactive)]
    [InlineData("False [0]", "Boolean", Iec61850ValueStatePresentation.Inactive)]
    [InlineData("OFF", "Boolean", Iec61850ValueStatePresentation.Inactive)]
    [InlineData("Open [01]", "Dbpos", Iec61850ValueStatePresentation.Inactive)]
    [InlineData("Intermediate [00]", "Dbpos", Iec61850ValueStatePresentation.Abnormal)]
    [InlineData("Bad state [11]", "Dbpos", Iec61850ValueStatePresentation.Abnormal)]
    public void DiscreteStates_AreClassifiedWithoutSeveritySemantics(string value, string dataType, string expected)
        => Assert.Equal(expected, Iec61850ValueStatePresentation.Classify(value, dataType));

    [Theory]
    [InlineData("1", "Boolean", Iec61850ValueStatePresentation.Active)]
    [InlineData("0", "Boolean", Iec61850ValueStatePresentation.Inactive)]
    [InlineData("1", "Float", Iec61850ValueStatePresentation.Neutral)]
    [InlineData("0", "INT32", Iec61850ValueStatePresentation.Neutral)]
    [InlineData("1.0", "Counter", Iec61850ValueStatePresentation.Neutral)]
    public void BareZeroOne_OnlyBecomeStateWhenMetadataProvesBoolean(string value, string dataType, string expected)
        => Assert.Equal(expected, Iec61850ValueStatePresentation.Classify(value, dataType));

    [Theory]
    [InlineData("True [1]", "Boolean", "Status", "IEDLD/GGIO1.SwLoc.stVal", Iec61850ValueStatePresentation.BooleanTrue)]
    [InlineData("False [0]", "Boolean", "Status", "IEDLD/GGIO1.SwLoc.stVal", Iec61850ValueStatePresentation.BooleanFalse)]
    [InlineData("Open [01]", "Enum", "Position", "IEDLD/CSWI1.Pos.stVal", Iec61850ValueStatePresentation.PositionOpen)]
    [InlineData("Close [10]", "Enum", "Position", "IEDLD/CSWI1.Pos.stVal", Iec61850ValueStatePresentation.PositionClose)]
    [InlineData("Intermediate [00]", "Enum", "Position", "IEDLD/XCBR1.Pos.stVal", Iec61850ValueStatePresentation.PositionIntermediate)]
    [InlineData("Bad state [11]", "Dbpos", "Position", "IEDLD/XCBR1.Pos.stVal", Iec61850ValueStatePresentation.PositionBad)]
    [InlineData("123.45", "Float32", "Measurement", "IEDLD/MMXU1.TotW.mag.f", Iec61850ValueStatePresentation.Analog)]
    [InlineData("A=0,B=0,C=0", "Unknown", "DataSet", "IEDLD/MHAI1.ThdA", Iec61850ValueStatePresentation.Analog)]
    public void VisualKind_DistinguishesOperatorValueFamilies(
        string value,
        string dataType,
        string category,
        string reference,
        string expected)
        => Assert.Equal(
            expected,
            Iec61850ValueStatePresentation.ClassifyVisualKind(value, dataType, category, reference));

    [Fact]
    public void VisualKind_DoesNotTurnGenericEnumOffIntoPosition()
    {
        var kind = Iec61850ValueStatePresentation.ClassifyVisualKind(
            "off",
            "Enum",
            "Status",
            "IEDLD/GGIO1.AutoMode.stVal");

        Assert.Equal(Iec61850ValueStatePresentation.Neutral, kind);
    }

    [Theory]
    [InlineData("FLOAT32", "F")]
    [InlineData("FLOAT64", "F")]
    [InlineData("floating-point", "F")]
    [InlineData("INT32", "I")]
    [InlineData("integer", "I")]
    [InlineData("INT16", "I")]
    [InlineData("INT32U", "U")]
    [InlineData("UINT16", "U")]
    [InlineData("BOOLEAN", "B")]
    [InlineData("SPS", "B")]
    [InlineData("SPC", "B")]
    [InlineData("SinglePointStatus", "B")]
    [InlineData("Enum", "E")]
    [InlineData("Dbpos", "DP")]
    [InlineData("DPC", "DP")]
    [InlineData("DPS", "DP")]
    [InlineData("Counter", "")]
    [InlineData("Unknown", "")]
    public void TypeToken_UsesDeclaredMetadataOnly(string dataType, string expected)
        => Assert.Equal(expected, Iec61850ValueStatePresentation.TypeToken(dataType));

    [Fact]
    public void EventAndLivePoint_UseSamePresentationClassifier()
    {
        var point = new Iec61850MonitorPoint { IecDataType = "Boolean", Value = "True" };
        var entry = new Iec61850EventEntry { IecDataType = "Boolean", NewValue = "True" };

        Assert.Equal(Iec61850ValueStatePresentation.Active, point.ValueTone);
        Assert.Equal(point.ValueTone, entry.ValueTone);
        Assert.Equal(Iec61850ValueStatePresentation.BooleanTrue, point.ValueVisualKind);
        Assert.Equal(point.ValueVisualKind, entry.ValueVisualKind);
        Assert.Equal("B", point.ValueTypeToken);
        Assert.Equal(point.ValueTypeToken, entry.ValueTypeToken);
        Assert.Equal("True", point.DisplayValue);
        Assert.Equal("True", entry.DisplayValue);
    }

    [Fact]
    public void MainWindow_UsesSemanticFamilyBadgesAcrossProcessValueSurfaces()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml"));

        Assert.Contains("x:Key=\"ProcessValueBadgeTemplate\"", source, StringComparison.Ordinal);
        Assert.True(Count(source, "CellTemplate=\"{StaticResource ProcessValueBadgeTemplate}\"") >= 3,
            "Explorer, Global Live Monitor and Event Log must share the same process-value badge template.");

        Assert.Contains("Text=\"{Binding ValueTypeToken}\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueTypeToken}\" Value=\"F\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueTypeToken}\" Value=\"I\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueTypeToken}\" Value=\"U\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueTypeToken}\" Value=\"B\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueTypeToken}\" Value=\"E\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueTypeToken}\" Value=\"DP\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueVisualKind}\" Value=\"BooleanTrue\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueVisualKind}\" Value=\"BooleanFalse\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueVisualKind}\" Value=\"PositionOpen\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueVisualKind}\" Value=\"PositionClose\"", source, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding ValueVisualKind}\" Value=\"PositionIntermediate\"", source, StringComparison.Ordinal);

        Assert.DoesNotContain("Property=\"Text\" Value=\"A\"", source, StringComparison.Ordinal);
        Assert.Contains("#F1EFFF", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#ECF8FF", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#EAF4FF", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#FFF8E6", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Type markers are metadata-only", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Closed/ON/true is red", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Open/OFF/false is green", source, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
