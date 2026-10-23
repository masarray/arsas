using ArIED61850Tester;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class Iec61850TimestampPresentationTests
{
    [Theory]
    [InlineData(2_004_000L, "31.200")]
    [InlineData(2_005_000L, "31.201")]
    [InlineData(2_006_000L, "31.201")]
    [InlineData(2_010_000L, "31.201")]
    public void MillisecondPresentation_RoundsInsteadOfTruncating(
        long fractionalTicks,
        string expected)
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 31, TimeSpan.Zero)
            .AddTicks(fractionalTicks);

        Assert.Equal(
            expected,
            Iec61850TimestampPresentation.FormatMilliseconds(timestamp, "ss.fff"));
    }

    [Fact]
    public void CustomerCase_31Point2006_IsPresentedAs31Point201()
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 31, TimeSpan.Zero)
            .AddTicks(2_006_000);

        Assert.Equal(
            "31.201",
            Iec61850TimestampPresentation.FormatMilliseconds(timestamp, "ss.fff"));
    }

    [Fact]
    public void LiveWorkspaceString_RoundsToThreeFractionalDigits_WithoutChangingSource()
    {
        const string fullPrecision = "2026-08-14 13:48:12.9165859";

        var display = Iec61850TimestampPresentation.FormatMilliseconds(fullPrecision);

        Assert.Equal("2026-08-14 13:48:12.917", display);
        Assert.Equal("2026-08-14 13:48:12.9165859", fullPrecision);
    }

    [Theory]
    [InlineData("-", "-")]
    [InlineData("relay timestamp unavailable", "relay timestamp unavailable")]
    public void LiveWorkspaceString_NonTimestampValues_AreNotInvented(string source, string expected)
        => Assert.Equal(expected, Iec61850TimestampPresentation.FormatMilliseconds(source));

    [Fact]
    public void Rounding_CarriesAcrossSecondAndMinuteBoundary()
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 59, TimeSpan.FromHours(7))
            .AddTicks(9_996_000);

        Assert.Equal(
            "12:01:00.000 +07:00",
            Iec61850TimestampPresentation.FormatMilliseconds(
                timestamp,
                "HH:mm:ss.fff zzz"));
    }

    [Fact]
    public void FullResolutionEvidence_RemainsUnchanged()
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 31, TimeSpan.Zero)
            .AddTicks(2_006_000);
        var evidence = new IoTestTransitionEvidence(
            Guid.NewGuid(),
            IoEvidenceTransition.On,
            false,
            true,
            "true",
            timestamp,
            timestamp,
            "Good",
            "BRCB",
            1,
            1,
            IoEvidenceVerdict.Accepted,
            "Accepted");

        _ = Iec61850TimestampPresentation.FormatMilliseconds(
            evidence.IedTimestamp,
            "ss.fff");

        Assert.Equal(timestamp, evidence.IedTimestamp);
        Assert.EndsWith(".2006000+00:00", evidence.IedTimestamp!.Value.ToString("O"));
    }

    [Fact]
    public void FatRuntime_UsesRoundedIedTimestamp()
    {
        var runtime = new IoTestPointRuntime();
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 31, TimeSpan.Zero)
            .AddTicks(2_006_000);

        var observation = new IoTestObservation(
            true,
            "true",
            timestamp,
            timestamp,
            "Good",
            "BRCB",
            1,
            1);

        typeof(IoTestPointRuntime)
            .GetMethod(
                "ApplyObservation",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(runtime, [observation]);

        Assert.Contains("31.201", runtime.CurrentIedTimestamp, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralIec61850ValueFormatter_UsesSameRounding()
    {
        var timestamp = new DateTimeOffset(2026, 8, 13, 12, 0, 31, TimeSpan.Zero)
            .AddTicks(2_006_000);

        Assert.Contains(
            "31.201",
            Iec61850ValueFormatter.Format(timestamp, "UtcTime", string.Empty),
            StringComparison.Ordinal);
    }
}
