using System.Globalization;
using System.Reflection;
using ArIED61850Tester.Models.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatTimestampTooltipTests
{
    [Fact]
    public void TimestampHover_ShowsRoundedDisplayAndDecodedFullPrecision()
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 31, TimeSpan.FromHours(7))
            .AddTicks(2_006_000);
        var evidence = new IoTestTransitionEvidence(
            Guid.NewGuid(),
            IoEvidenceTransition.On,
            false,
            true,
            "true",
            timestamp.AddMilliseconds(1),
            timestamp,
            "Good",
            "BRCB",
            1,
            1,
            IoEvidenceVerdict.Accepted,
            "Accepted");

        var method = typeof(IoTestPointRuntime).GetMethod(
            "BuildEvidenceToolTip",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(IoTestPointRuntime), "BuildEvidenceToolTip");
        var tooltip = (string)method.Invoke(null, [evidence, "ON"])!;

        Assert.Contains("Displayed (rounded to nearest ms): 2026-08-13 12:00:31.201 +07:00", tooltip, StringComparison.Ordinal);
        Assert.Contains(
            "Decoded IED timestamp (full precision): " + timestamp.ToString("O", CultureInfo.InvariantCulture),
            tooltip,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Raw IED timestamp", tooltip, StringComparison.Ordinal);
        Assert.Contains("ARSAS capture (full precision):", tooltip, StringComparison.Ordinal);
        Assert.Contains("Quality: Good", tooltip, StringComparison.Ordinal);
        Assert.Contains("Source: BRCB", tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotTimestampHover_ShowsRoundedDisplayAndDecodedFullPrecision()
    {
        var timestamp = new DateTimeOffset(2026, 9, 3, 12, 43, 16, TimeSpan.FromHours(7))
            .AddTicks(9_215_108);
        var evidence = new FatValueEvidence(
            Guid.NewGuid(),
            FatValueSlot.Value1,
            FatEvidenceCaptureKind.OperatorSnapshot,
            "12.345",
            timestamp.AddMilliseconds(2),
            timestamp,
            "good",
            "ARIEC Hybrid: StaticBrcb",
            7,
            3);

        var method = typeof(IoTestPointRuntime).GetMethod(
            "BuildFatEvidenceToolTip",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(IoTestPointRuntime), "BuildFatEvidenceToolTip");
        var tooltip = (string)method.Invoke(null, [evidence, "Value 1"])!;

        Assert.Contains("Value 1: 12.345", tooltip, StringComparison.Ordinal);
        Assert.Contains("Displayed (rounded to nearest ms): 2026-09-03 12:43:16.922 +07:00", tooltip, StringComparison.Ordinal);
        Assert.Contains(
            "Decoded IED timestamp (full precision): " + timestamp.ToString("O", CultureInfo.InvariantCulture),
            tooltip,
            StringComparison.Ordinal);
        Assert.DoesNotContain("16.9220000", tooltip, StringComparison.Ordinal);
        Assert.Contains("ARSAS capture (full precision):", tooltip, StringComparison.Ordinal);
        Assert.Contains("Quality: good", tooltip, StringComparison.Ordinal);
        Assert.Contains("Source: ARIEC Hybrid: StaticBrcb", tooltip, StringComparison.Ordinal);
        Assert.Contains("Capture: OperatorSnapshot", tooltip, StringComparison.Ordinal);
    }
}
