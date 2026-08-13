using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class LivePowerMeasurementRegressionTests
{
    [Theory]
    [InlineData("IEDLD/MMXU1.TotW.mag.f")]
    [InlineData("IEDLD/MMXU1.TotW.instMag.f")]
    [InlineData("IEDLD/MMXU1.TotVAr.mag.f")]
    [InlineData("IEDLD/MMXU1.TotVA.mag.f")]
    [InlineData("IEDLD/MMXU1.TotPF.mag.f")]
    [InlineData("IEDLD/MMXU1.Hz.mag.f")]
    [InlineData("IEDLD/MMXU1.W.phsA.cVal.mag.f")]
    [InlineData("IEDLD/MMXU1.VAr.phsA.cVal.mag.f")]
    [InlineData("IEDLD/MMXU1.VA.phsA.cVal.mag.f")]
    [InlineData("IEDLD/MMXU1.PF.phsA.cVal.mag.f")]
    public void FundamentalPower_IsPublishableOperatorMeasurement(string reference)
    {
        var signal = NewMeasurement(reference);
        Assert.False(SignalDefinition.IsStatisticsOrHarmonicNoise(reference));
        Assert.True(signal.CanPublishAsSignal);
        Assert.True(SasOperationalSignalPolicy.IsOperationalValue(signal));
    }

    [Theory]
    [InlineData("IEDLD/Har2MMXU1.A.phsA.cVal.mag.f")]
    [InlineData("IEDLD/MMXU1.THD.mag.f")]
    [InlineData("IEDLD/MMXU1.MeanA.mag.f")]
    [InlineData("IEDLD/MMXU1.DmdW.mag.f")]
    public void StatisticsAndHarmonics_RemainNoise(string reference)
    {
        Assert.True(SignalDefinition.IsStatisticsOrHarmonicNoise(reference));
    }

    [Fact]
    public void NativeShallowMmxuPowerObjects_AreRecoveredWithCorrectUnits()
    {
        var snapshot = new NativeMmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["IEDLD"] = new[]
                {
                    "MMXU1$MX$TotW",
                    "MMXU1$MX$TotVAr",
                    "MMXU1$MX$TotVA",
                    "MMXU1$MX$TotPF",
                    "MMXU1$MX$Hz"
                }
            }
        };

        var signals = NativeMmsDiscoveryMapper.BuildSignals(snapshot);
        AssertMeasurement(signals, "IEDLD/MMXU1.TotW.mag.f", "W");
        AssertMeasurement(signals, "IEDLD/MMXU1.TotVAr.mag.f", "VAr");
        AssertMeasurement(signals, "IEDLD/MMXU1.TotVA.mag.f", "VA");
        AssertMeasurement(signals, "IEDLD/MMXU1.TotPF.mag.f", string.Empty);
        AssertMeasurement(signals, "IEDLD/MMXU1.Hz.mag.f", "Hz");
    }

    [Fact]
    public void NativeInstantMagnitude_UsesDataObjectCompanionReferences()
    {
        var snapshot = new NativeMmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["IEDLD"] = new[] { "MMXU1$MX$TotW$instMag$f" }
            }
        };

        var signal = Assert.Single(NativeMmsDiscoveryMapper.BuildSignals(snapshot)
            .Where(item => item.ObjectReference.Equals("IEDLD/MMXU1.TotW.instMag.f", StringComparison.OrdinalIgnoreCase)));

        Assert.Equal("Measurement", signal.Category);
        Assert.Equal("W", signal.Unit);
        Assert.Equal("IEDLD/MMXU1.TotW.q", signal.QualityReference);
        Assert.Equal("IEDLD/MMXU1.TotW.t", signal.TimestampReference);
    }

    [Fact]
    public void ScalarMagnitudeResolver_KeepsMagAndInstMagSiblingFallback()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IecSignalReadResolver.cs"));
        Assert.Contains("ReplaceToken(reference, \".instMag.f\", \".mag.f\")", source, StringComparison.Ordinal);
        Assert.Contains("ReplaceToken(reference, \".mag.f\", \".instMag.f\")", source, StringComparison.Ordinal);
        Assert.Contains("\".instMag.f\", \".mag.f\"", source, StringComparison.Ordinal);
    }

    private static SignalDefinition NewMeasurement(string reference) => new()
    {
        Name = reference,
        ObjectReference = reference,
        FunctionalConstraint = "MX",
        DataType = "Float32",
        Category = "Measurement",
        Source = "SCL design model",
        ProbeStatus = "Readable",
        Value = "0",
        Quality = "Good"
    };

    private static void AssertMeasurement(IReadOnlyList<SignalDefinition> signals, string reference, string unit)
    {
        var signal = Assert.Single(signals.Where(item =>
            item.ObjectReference.Equals(reference, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("MX", signal.FunctionalConstraint);
        Assert.Equal("Float32", signal.DataType);
        Assert.Equal("Measurement", signal.Category);
        Assert.Equal(unit, signal.Unit);
        Assert.True(signal.CanPublishAsSignal);
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
        throw new FileNotFoundException(relativePath);
    }
}
