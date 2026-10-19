using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217MFocusedAdmissionBehaviorTests
{
    [Fact]
    public void QueuedOrBusyDoesNotConsumeAttemptAndCompletionStopsRepeatedRead()
    {
        var admission = new IedBrowserFocusedAdmission();
        var key = IedBrowserFocusedAdmission.PageKey("model/ln/LD/XCBR1",
            "LD/XCBR1.Pos");
        Assert.True(admission.CanStart("dev", 10, key, 2));
        Assert.Equal(0, admission.Attempts(key)); // Dispatch skipped
        Assert.True(admission.TryStart("dev", 10, key, 2));
        Assert.False(admission.TryStart("dev", 10, key, 1));
        Assert.Equal(1, admission.Attempts(key));
        Assert.True(admission.TryStart("dev", 10, key, 2));
        Assert.False(admission.CanStart("dev", 10, key, 2));
        admission.Complete(key);
        Assert.False(admission.CanStart("dev", 10, key, 3));
        Assert.True(admission.CanStart("dev", 11, key, 2));
        Assert.Equal(0, admission.Attempts(key));
        Assert.True(admission.CanStart("otherDev", 11, key, 2));
    }

    [Fact]
    public void DifferentDoKeysAreIsolatedAndHugeBrowseHistoryRemainsBounded()
    {
        var admission = new IedBrowserFocusedAdmission();
        var pos = IedBrowserFocusedAdmission.PageKey("LN", "LD/XCBR1.Pos");
        var beh = IedBrowserFocusedAdmission.PageKey("LN", "LD/XCBR1.Beh");
        Assert.True(admission.TryStart("dev", 3, pos, 1));
        Assert.False(admission.CanStart("dev", 3, pos, 1));
        Assert.True(admission.TryStart("dev", 3, beh, 1));
        for (var i = 0; i < 1000; i++)
            Assert.True(admission.TryStart("dev", 3, "LN" + i, 1));
        Assert.True(admission.CanStart("dev", 4, pos, 1)); // reconnect
    }

    [Fact]
    public void ReportObservedDataModelLeafSuppressesOnlyItsExactReadGap()
    {
        const string owner = "BCUGEF650/MMXU1.TotW";
        LiveIedDataAttributeModel Attr(string suffix, string fc) => new()
        {
            ObjectReference = owner, AttributePath = "TotW." + suffix,
            FunctionalConstraint = fc,
            MmsReference = "BCUGEF650/MMXU1$" + fc + "$TotW$" +
                suffix.Replace('.', '$'),
            SclBType = "Float32"
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "BCUGEF650",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "MMXU1",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "TotW", Reference = owner,
                        Attributes = [Attr("mag.f", "MX"), Attr("q", "MX"),
                            Attr("t", "MX"), Attr("units.SIUnit", "CF"),
                            Attr("units.multiplier", "CF")]
                    }]
                }]
            }]
        };
        var ln = Assert.Single(Assert.Single(model.LogicalDevices).LogicalNodes);
        var page = new IedBrowserTreeRow("model/ln/BCUGEF650/MMXU1",
            "MMXU1", "LN", 4, ln);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association, owner + ".mag.f",
            "MX", "0", "", "", "Report"));
        var gaps = IedBrowserReadPlan.BuildFocused(model, page, ledger,
            selectedDoReference: owner);
        Assert.DoesNotContain(gaps, x => x.Reference.EndsWith(
            "$MX$TotW$mag$f", StringComparison.Ordinal));
        Assert.Contains(gaps, x => x.Reference.EndsWith(
            "$CF$TotW$units$SIUnit", StringComparison.Ordinal));
        Assert.Contains(gaps, x => x.Reference.EndsWith(
            "$CF$TotW$units$multiplier", StringComparison.Ordinal));
        Assert.True(ledger.PublishForAssociation(association, owner + ".units.SIUnit",
            "CF", "2", "", "", "MMS Read"));
        Assert.True(ledger.TryGet(owner + ".units.SIUnit", "CF", out var actual));
        Assert.Equal("MMS Read", actual.Source);
        Assert.Equal("2", actual.Value);
        Assert.False(ledger.TryGet(owner + ".units.SIUnit", "MX", out _));
        ledger.BeginAssociation();
        Assert.False(ledger.TryGet(owner + ".units.SIUnit", "CF", out _));
    }
    [Fact]
    public void DataModelDoAndDaPagesCanAutoReadExactVerifiedLeafAndCf()
    {
        const string owner = "GE650/MMXU1.Hz";
        LiveIedDataAttributeModel Attr(string relative, string fc) => new()
        {
            ObjectReference = owner, AttributePath = "Hz." + relative,
            FunctionalConstraint = fc,
            MmsReference = "GE650/MMXU1$" + fc + "$Hz$" +
                relative.Replace('.', '$'),
            SclBType = "Float32"
        };
        var magnitude = Attr("mag.f", "MX");
        var si = Attr("units.SIUnit", "CF");
        var obj = new LiveIedDataObjectModel
        {
            Name = "Hz", Reference = owner,
            Attributes = [magnitude, si, Attr("units.multiplier", "CF")]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "GE650",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "MMXU1", DataObjects = [obj]
                }]
            }]
        };
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var doPage = new IedBrowserTreeRow("model/do/" + owner,
            "Hz", "DO", 3, obj);
        var all = IedBrowserReadPlan.BuildFocused(model, doPage, ledger);
        Assert.Equal(3, all.Count);
        Assert.Contains(all, t => t.FunctionalConstraint == "CF" &&
            t.Reference.EndsWith("$units$multiplier", StringComparison.Ordinal));
        var daPage = new IedBrowserTreeRow("model/da/" + si.MmsReference,
            "SIUnit", "DA", 4, si);
        var selected = IedBrowserReadPlan.BuildFocused(model, daPage, ledger);
        Assert.Single(selected);
        Assert.Equal(si.MmsReference, selected[0].Reference);
        Assert.True(ledger.PublishForAssociation(association, si.MmsReference,
            "CF", "33", "", "", "MMS Read"));
        Assert.Empty(IedBrowserReadPlan.BuildFocused(model, daPage, ledger));
        Assert.Equal(2, IedBrowserReadPlan.BuildFocused(
            model, doPage, ledger).Count);
    }

}
