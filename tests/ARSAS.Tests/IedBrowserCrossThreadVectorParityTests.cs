using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Stacked M2.13/M2.14 integration parity: extra neutral/residual vector rows
/// must obey the same timestamp/source/association gate as the three-phase MX composer.
/// No network access, SSOT mutation outside canonical observations or engine pin change.
/// </summary>
public sealed class IedBrowserCrossThreadVectorParityTests
{
    private const string Owner = "LD/MMXU1.PhV";
    private const string Mag = Owner + ".phsC.cVal.mag.f";
    private const string Ang = Owner + ".phsC.cVal.ang.f";

    private static IedBrowserVectorComponent Component()
        => new("phsC", Mag, Ang, "MX");

    private static IReadOnlyDictionary<string, string?> Units() =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Owner + ".phsC|MX"] = "kV",
            [Ang + "|MX"] = "°"
        };

    [Fact]
    public void DifferentTimestampNeverRendersFalsePairedPhasor()
    {
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(association, Mag, "MX",
            "12", "", "t1", "Report"));
        Assert.True(values.PublishForAssociation(association, Ang, "MX",
            "33", "", "t2", "Report"));
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, [Component()], Units(), true, out var rendered));
        Assert.Equal("phsC: 12 kV", rendered.Value);
        Assert.Contains("not paired with magnitude", rendered.Provenance);
        Assert.DoesNotContain(" kV ∠ ", rendered.Value);
        Assert.Contains(Mag, rendered.Provenance);
        Assert.Contains(Ang, rendered.Provenance);
    }

    [Fact]
    public void MixedReportAndMmsReadPreserveBothValuesButNeverPair()
    {
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        values.PublishForAssociation(association, Mag, "MX",
            "0", "", "t1", "Report");
        values.PublishForAssociation(association, Ang, "MX",
            "40", "", "t1", "MMS Read");
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, [Component()], Units(), true, out var rendered));
        Assert.Equal("phsC: 0 kV", rendered.Value);
        Assert.Contains("not paired with magnitude", rendered.Provenance);
        Assert.Equal("Report + MMS Read", rendered.Source);
        Assert.DoesNotContain(" kV ∠ ", rendered.Value);
    }

    [Fact]
    public void MatchedActualMagnitudeAndAngleAllowPhasorWithNoExtraReads()
    {
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        values.PublishForAssociation(association, Mag, "MX", "0", "", "t1", "Report");
        values.PublishForAssociation(association, Ang, "MX", "0", "", "t1", "Report");
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, [Component()], Units(), true, out var matched));
        Assert.Equal("phsC: 0 kV ∠ 0°", matched.Value);
        Assert.Equal("Report", matched.Source);
    }

    [Fact]
    public void AngleOnlyOrInvalidNumericNeverCreatesMagnitudeOrPolarPair()
    {
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        values.PublishForAssociation(association, Ang, "MX",
            "17", "", "", "Report");
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, [Component()], Units(), true, out var angleOnly));
        Assert.Equal("phsC: 17°", angleOnly.Value);
        Assert.Contains("magnitude not observed", angleOnly.Provenance);
        Assert.DoesNotContain(" ∠ 17°", angleOnly.Value);
        values.PublishForAssociation(association, Mag, "MX",
            "NaN", "", "", "Report");
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, [Component()], Units(), true, out var stillOnlyAngle));
        Assert.DoesNotContain("NaN", stillOnlyAngle.Value);
    }

    [Fact]
    public void FourModelVerifiedComponentsIncludeNeutralButRespectPerPairCompatibility()
    {
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        var parts = new List<IedBrowserVectorComponent>();
        foreach (var name in new[] { "phsA", "phsB", "phsC", "neut" })
        {
            var owner = Owner + "." + name + ".cVal";
            var mag = owner + ".mag.f";
            var ang = owner + ".ang.f";
            parts.Add(new IedBrowserVectorComponent(name, mag, ang, "MX"));
            values.PublishForAssociation(association, mag, "MX",
                "12", "", "t1", "Report");
            values.PublishForAssociation(association, ang, "MX",
                "17", "", name == "neut" ? "t2" : "t1", "Report");
        }
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, parts, new Dictionary<string, string?>(), true, out var result));
        Assert.Contains("phsA: 12 ∠ 17", result.Value);
        Assert.Contains("phsB: 12 ∠ 17", result.Value);
        Assert.Contains("phsC: 12 ∠ 17", result.Value);
        Assert.Contains("neut: 12", result.Value);
        Assert.Contains("neut: angle not paired with magnitude", result.Provenance);
        Assert.DoesNotContain("neut: 12 ∠", result.Value);
        Assert.DoesNotContain("independent", result.Value, StringComparison.OrdinalIgnoreCase);
        values.MarkOffline();
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, parts, new Dictionary<string, string?>(), true, out var stale));
        Assert.True(stale.IsStale);
        values.BeginAssociation();
        Assert.False(IedBrowserObservedVectorPresentation.TryFormat(
            values, parts, new Dictionary<string, string?>(), true, out _));
    }
}
