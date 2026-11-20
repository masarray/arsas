using System.Collections.Concurrent;
using System.Globalization;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// M2.13K + M2.14 exact-head integration regression. These tests simulate
/// independent RCB ingress lanes and explicit read producers against the
/// actual association-fenced canonical value ledger. They are NOT a claim
/// about physical MMS/RCB throughput or WPF dispatcher p95 latency.
/// </summary>
public sealed class M213KCrossThreadSsotStressTests
{
    private static string Key(int rcb, int signal)
        => "LD/MMXU1.RCB" + rcb.ToString(CultureInfo.InvariantCulture) +
           ".Point" + signal.ToString(CultureInfo.InvariantCulture) + ".mag.f";

    [Fact]
    public void ParallelMultiRcbBurstRetainsReportAuthorityAfterLateRead()
    {
        const int rcbs = 8, signalsPerRcb = 12, iterations = 32;
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var rejected = new ConcurrentBag<string>();

        Parallel.For(0, rcbs, rcb =>
        {
            for (var point = 0; point < signalsPerRcb; point++)
            {
                var reference = Key(rcb, point);
                for (var sequence = 0; sequence < iterations; sequence++)
                {
                    if (!ledger.PublishForAssociation(
                            association, reference, "MX",
                            sequence.ToString(CultureInfo.InvariantCulture),
                            "good", "sample-" + sequence,
                            "Report BRCB-" + rcb))
                        rejected.Add("Report " + reference);
                    // A late explicit Read on the same exact reference is a
                    // separate channel and must not overwrite Report authority.
                    if (!ledger.PublishForAssociation(
                            association, reference, "MX", "999999",
                            "good", "read-" + sequence, "MMS Read"))
                        rejected.Add("Read " + reference);
                }
            }
        });

        Assert.Empty(rejected);
        for (var rcb = 0; rcb < rcbs; rcb++)
        for (var point = 0; point < signalsPerRcb; point++)
        {
            var reference = Key(rcb, point);
            Assert.True(ledger.TryGet(reference, "MX",
                out var reported, preferReport: true));
            Assert.Equal((iterations - 1).ToString(CultureInfo.InvariantCulture),
                reported.Value);
            Assert.Equal("Report BRCB-" + rcb, reported.Source);
            Assert.Equal(association, reported.Association);
            Assert.False(reported.IsStale);
            Assert.True(ledger.TryGet(reference, "MX",
                out var latest, preferReport: false));
            Assert.Equal("999999", latest.Value);
            Assert.Equal("MMS Read", latest.Source);
        }
        // The ledger does not produce phantom measurement rows or phase data.
        Assert.False(ledger.TryGet("LD/MMXU1.RCB0.Point0.ang.f",
            "MX", out _));
        Assert.False(ledger.TryGet(Key(0, 0), "ST", out _));
    }

    [Fact]
    public void RepeatedOfflineReconnectNeverAdmitsOldAssociationCallbacks()
    {
        var ledger = new IedBrowserLiveValues();
        const string reference = "LD/MMXU1.PhV.phsA.cVal.mag.f";
        var previousAssociation = -1L;
        for (var cycle = 0; cycle < 128; cycle++)
        {
            var association = ledger.BeginAssociation();
            Assert.True(association > previousAssociation);
            Assert.False(ledger.TryGet(reference, "MX", out _));
            if (previousAssociation >= 0)
            {
                Assert.False(ledger.PublishForAssociation(previousAssociation,
                    reference, "MX", "7777", "", "", "Report"));
            }
            Assert.True(ledger.PublishForAssociation(association, reference,
                "MX", cycle.ToString(CultureInfo.InvariantCulture),
                "good", "cycle-" + cycle, "Report"));
            Assert.True(ledger.TryGet(reference, "MX", out var current));
            Assert.Equal(association, current.Association);
            Assert.Equal(cycle.ToString(CultureInfo.InvariantCulture),
                current.Value);
            Assert.False(current.IsStale);
            ledger.MarkOffline();
            Assert.True(ledger.TryGet(reference, "MX", out var offline));
            Assert.True(offline.IsStale);
            Assert.False(ledger.PublishForAssociation(association,
                reference, "MX", "8888", "", "", "Report"));
            previousAssociation = association;
        }
        ledger.BeginAssociation();
        Assert.False(ledger.TryGet(reference, "MX", out _));
    }

    [Fact]
    public void ExactCaseAndFunctionalConstraintRemainDistinctAcrossTraffic()
    {
        const string uppercase = "LD/MMXU1.PhV.phsA.cVal.mag.f";
        const string lowercase = "LD/MMXU1.PhV.phsa.cVal.mag.f";
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var keys = new[]
        {
            (Reference: uppercase, Fc: "MX", Value: "10"),
            (Reference: lowercase, Fc: "MX", Value: "20"),
            (Reference: uppercase, Fc: "ST", Value: "30"),
            (Reference: lowercase, Fc: "ST", Value: "40")
        };
        Parallel.ForEach(keys, item =>
        {
            for (var i = 0; i < 32; i++)
                ledger.PublishForAssociation(association, item.Reference,
                    item.Fc, item.Value, "", "", "Report");
        });
        foreach (var key in keys)
        {
            Assert.True(ledger.TryGet(key.Reference, key.Fc,
                out var value, preferReport: true));
            Assert.Equal(key.Value, value.Value);
            Assert.Equal(association, value.Association);
        }
        Assert.False(ledger.TryGet("LD/MMXU1.PhV.PhSA.cVal.mag.f",
            "MX", out _));
        Assert.False(ledger.TryGet("LD/MMXU1.PhV.phsA.cVal.ang.f",
            "MX", out _));
    }
}
