using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class ProcessValueStatePresentationTests
{
    [Theory]
    [InlineData("True", "Boolean", Iec61850ValueStatePresentation.Active)]
    [InlineData("ON", "Boolean", Iec61850ValueStatePresentation.Active)]
    [InlineData("Closed [10]", "Dbpos", Iec61850ValueStatePresentation.Active)]
    [InlineData("False", "Boolean", Iec61850ValueStatePresentation.Inactive)]
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

    [Fact]
    public void EventAndLivePoint_UseSamePresentationClassifier()
    {
        var point = new Iec61850MonitorPoint { IecDataType = "Boolean", Value = "True" };
        var entry = new Iec61850EventEntry { IecDataType = "Boolean", NewValue = "True" };

        Assert.Equal(Iec61850ValueStatePresentation.Active, point.ValueTone);
        Assert.Equal(point.ValueTone, entry.ValueTone);
        Assert.Equal("True", point.DisplayValue);
        Assert.Equal("True", entry.DisplayValue);
    }

    [Fact]
    public void MainWindow_UsesPremiumBlueSlateStateBadgesAcrossProcessValueSurfaces()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml"));

        Assert.Contains("x:Key=\"ProcessValueBadgeTemplate\"", source, StringComparison.Ordinal);
        Assert.True(Count(source, "CellTemplate=\"{StaticResource ProcessValueBadgeTemplate}\"") >= 3,
            "Explorer, Global Live Monitor and Event Log must share the same process-value badge template.");
        Assert.Contains("Value=\"Active\"", source, StringComparison.Ordinal);
        Assert.Contains("Value=\"Inactive\"", source, StringComparison.Ordinal);
        Assert.Contains("#EAF4FF", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#245F9E", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#F3F6F9", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#617286", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Color describes state, not alarm severity", source, StringComparison.OrdinalIgnoreCase);
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
