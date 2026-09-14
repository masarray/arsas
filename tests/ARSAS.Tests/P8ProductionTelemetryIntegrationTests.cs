using System.Reflection;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P8ProductionTelemetryIntegrationTests
{
    [Fact]
    public void ProductionNormalizer_MissingQualityIsNotGood_AndMalformedTimestampStaysUnknown()
    {
        var envelope = Iec61850ProductionTelemetryNormalizer.FromComponents(
            value: true,
            displayValue: "True",
            quality: null,
            deviceTimestamp: "10:00:31",
            receivedAtUtc: new DateTimeOffset(2026, 9, 11, 3, 0, 0, TimeSpan.Zero),
            sourceReference: "LD0/LLN0.Mod.stVal");

        Assert.Equal(Iec61850TelemetryQualityState.Questionable, envelope.QualityState);
        Assert.Equal("Unknown", envelope.QualityText);
        Assert.NotEqual(Iec61850TelemetryQualityState.Good, envelope.QualityState);
        Assert.Null(envelope.SourceTimestampUtc);
        Assert.Equal("-", Iec61850ProductionTelemetryNormalizer.SourceTimestampTextOrUnknown(envelope, "10:00:31"));
    }

    [Fact]
    public void ProductionNormalizer_ExplicitGoodAndCompleteRelayTimestampPassThrough()
    {
        const string relayTimestamp = "2026-09-11T03:04:05.125Z";
        var envelope = Iec61850ProductionTelemetryNormalizer.FromComponents(
            value: 1,
            displayValue: "1",
            quality: "Good",
            deviceTimestamp: relayTimestamp,
            receivedAtUtc: new DateTimeOffset(2026, 9, 11, 3, 4, 6, TimeSpan.Zero),
            sourceReference: "LD0/LLN0.Mod.stVal");

        Assert.True(envelope.IsValid);
        Assert.Equal(Iec61850TelemetryQualityState.Good, envelope.QualityState);
        Assert.Equal("Good", envelope.QualityText);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 3, 4, 5, 125, TimeSpan.Zero), envelope.SourceTimestampUtc);
        Assert.Equal(relayTimestamp, Iec61850ProductionTelemetryNormalizer.SourceTimestampTextOrUnknown(envelope, relayTimestamp));
    }

    [Fact]
    public void DiscoveryProductionPath_DoesNotPromoteMissingQualityToGood()
    {
        var signal = new SignalDefinition
        {
            Name = "Mod",
            ObjectReference = "LD0/LLN0.Mod.stVal",
            DataType = "BOOLEAN"
        };
        var read = new Iec61850ReadValue
        {
            Value = true,
            DisplayValue = "True",
            Quality = string.Empty,
            DeviceTimestamp = "10:00:31",
            SourceReference = signal.ObjectReference,
            ReceivedAtUtc = new DateTimeOffset(2026, 9, 11, 3, 0, 0, TimeSpan.Zero)
        };

        var method = typeof(NativeIec61850Client).GetMethod(
            "ApplyDiscoveryReadValue",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, [signal, read]);

        Assert.Equal("Unknown", signal.Quality);
        Assert.False(signal.Quality.Equals("Good", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("-", signal.DeviceTimestamp);
        Assert.Equal("True", signal.Value);
    }

    [Fact]
    public void RuntimeSource_UsesProductionEnvelopeBeforeStateSnapshotAndSoe()
    {
        var source = ReadRepoFile("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains("Iec61850ProductionTelemetryNormalizer.FromComponents", source, StringComparison.Ordinal);
        Assert.Contains("hasProcessValue: envelope.HasProcessValue", source, StringComparison.Ordinal);
        Assert.Contains("hasProcessValue: reportEnvelope.HasProcessValue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var quality = rich?.HasQuality == true ? rich.Quality : state.Quality;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var deviceTimestamp = rich?.HasDeviceTimestamp == true ? rich.DeviceTimestamp : state.DeviceTimestamp;", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
