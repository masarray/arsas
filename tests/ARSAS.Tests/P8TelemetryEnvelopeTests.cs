using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P8TelemetryEnvelopeTests
{
    [Fact]
    public void NullRead_IsInvalid_AndDoesNotInventSourceTimestamp()
    {
        var received = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

        var envelope = Iec61850TelemetryEnvelope.FromReadValue(null, received);

        Assert.False(envelope.IsValid);
        Assert.False(envelope.IsUsable);
        Assert.Equal(Iec61850TelemetryQualityState.Invalid, envelope.QualityState);
        Assert.Null(envelope.SourceTimestampUtc);
        Assert.Equal(received, envelope.ReceivedAtUtc);
    }

    [Fact]
    public void NormalizationWithoutOverride_PreservesReadReceiptTime()
    {
        var received = new DateTimeOffset(2026, 9, 11, 1, 15, 30, TimeSpan.Zero);
        var read = new Iec61850ReadValue
        {
            Value = true,
            DisplayValue = "True",
            Quality = "Good",
            ReceivedAtUtc = received
        };

        var envelope = Iec61850TelemetryEnvelope.FromReadValue(read);

        Assert.Equal(received, envelope.ReceivedAtUtc);
    }

    [Fact]
    public void MissingProcessValue_RemainsInvalid_EvenWhenQualitySaysGood()
    {
        var read = new Iec61850ReadValue
        {
            Value = null,
            DisplayValue = "-",
            Quality = "Good",
            DeviceTimestamp = "2026-09-11T01:02:03Z"
        };

        var envelope = read.ToTelemetryEnvelope();

        Assert.False(envelope.IsValid);
        Assert.False(envelope.IsUsable);
        Assert.False(envelope.HasProcessValue);
        Assert.Equal(Iec61850TelemetryQualityState.Invalid, envelope.QualityState);
    }

    [Fact]
    public void MalformedRelayTimestamp_RemainsUnknown_InsteadOfUsingPcClock()
    {
        var received = new DateTimeOffset(2026, 9, 11, 2, 0, 0, TimeSpan.Zero);
        var read = new Iec61850ReadValue
        {
            Value = true,
            DisplayValue = "True",
            Quality = "Good",
            DeviceTimestamp = "not-a-relay-timestamp",
            ReceivedAtUtc = received
        };

        var envelope = read.ToTelemetryEnvelope();

        Assert.True(envelope.IsValid);
        Assert.True(envelope.IsUsable);
        Assert.Null(envelope.SourceTimestampUtc);
        Assert.Equal(received, envelope.ReceivedAtUtc);
        Assert.Contains("could not be parsed", envelope.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GoodTelemetry_PreservesSourceTimestampSeparatelyFromReceiptTime()
    {
        var received = new DateTimeOffset(2026, 9, 11, 2, 0, 0, TimeSpan.Zero);
        var read = new Iec61850ReadValue
        {
            Value = 1,
            DisplayValue = "1",
            Quality = "Good",
            DeviceTimestamp = "2026-09-11T01:02:03.125Z",
            SourceReference = "LD0/LLN0.Mod.stVal",
            ReceivedAtUtc = received
        };

        var envelope = read.ToTelemetryEnvelope();

        Assert.True(envelope.IsValid);
        Assert.True(envelope.IsUsable);
        Assert.Equal(Iec61850TelemetryQualityState.Good, envelope.QualityState);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 1, 2, 3, 125, TimeSpan.Zero), envelope.SourceTimestampUtc);
        Assert.Equal(received, envelope.ReceivedAtUtc);
        Assert.Equal("LD0/LLN0.Mod.stVal", envelope.SourceReference);
    }

    [Fact]
    public void ZoneLessArIecUtcTimeDisplay_IsInterpretedAsUtc_NotLocalPcTime()
    {
        var read = new Iec61850ReadValue
        {
            Value = 1,
            DisplayValue = "1",
            Quality = "Good",
            DeviceTimestamp = "2026-08-13 10:00:31.2006000"
        };

        var envelope = read.ToTelemetryEnvelope();

        Assert.NotNull(envelope.SourceTimestampUtc);
        Assert.Equal(TimeSpan.Zero, envelope.SourceTimestampUtc!.Value.Offset);
        Assert.Equal(2026, envelope.SourceTimestampUtc.Value.Year);
        Assert.Equal(10, envelope.SourceTimestampUtc.Value.Hour);
    }

    [Theory]
    [InlineData("Invalid")]
    [InlineData("Bad / communication error")]
    [InlineData("Failure")]
    [InlineData("Reserved")]
    public void DegradedQuality_IsNeverPromotedToGood(string quality)
    {
        var envelope = Iec61850TelemetryEnvelope.FromReadValue(new Iec61850ReadValue
        {
            Value = 1,
            DisplayValue = "1",
            Quality = quality
        });

        Assert.Equal(Iec61850TelemetryQualityState.Invalid, envelope.QualityState);
        Assert.False(envelope.IsValid);
        Assert.False(envelope.IsUsable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    [InlineData("Questionable")]
    [InlineData("vendor-future-token")]
    public void UnprovenQuality_IsQuestionable_NeverSilentlyGood(string quality)
    {
        var envelope = Iec61850TelemetryEnvelope.FromReadValue(new Iec61850ReadValue
        {
            Value = 1,
            DisplayValue = "1",
            Quality = quality
        });

        Assert.Equal(Iec61850TelemetryQualityState.Questionable, envelope.QualityState);
        Assert.False(envelope.IsValid);
        Assert.True(envelope.IsUsable);
        Assert.NotEqual(Iec61850TelemetryQualityState.Good, envelope.QualityState);
        Assert.Contains("not proven Good", envelope.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }
}
