using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217DRcbAssociationAndAnalogFcParityTests
{
    private const string Owner = "LD/MMXU1.PhV";
    private const string DataSet = "LD/LLN0.Phasors";

    private static LiveIedDataAttributeModel Attribute(string phase, string fc = "MX")
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "PhV." + phase + ".cVal.mag.f",
            MmsReference = "LD/MMXU1$" + fc + "$PhV$" + phase + "$cVal$mag$f",
            FunctionalConstraint = fc,
            SclBType = "FLOAT32"
        };

    private static LiveIedModelDiscoveryDocument Model(bool conflicting = true)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "MMXU1", DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "PhV", Reference = Owner, InferredCdc = "WYE",
                        Attributes = [
                            Attribute("phsA"), Attribute("phsB"), Attribute("phsC")
                        ]
                    }]
                }]
            }],
            DataSets = [new LiveIedDataSetModel
            {
                Reference = DataSet, Domain = "LD", LogicalNode = "LLN0",
                Name = "Phasors", MemberCount = conflicting ? 2 : 1,
                Members = conflicting
                    ? [new LiveIedDataSetMemberModel
                        {
                            Reference = Owner, FunctionalConstraint = "MX"
                        },
                        new LiveIedDataSetMemberModel
                        {
                            Reference = Owner + ".phsA.cVal.mag.f",
                            FunctionalConstraint = "ST"
                        }]
                    : [new LiveIedDataSetMemberModel
                        {
                            Reference = Owner, FunctionalConstraint = "MX"
                        }]
            }]
        };

    [Fact]
    public void RcbReadOnlySnapshotCannotCrossReconnectWithoutChangingIEDCard()
    {
        var model = Model(false);
        var device = new Iec61850MonitorDevice
        {
            LiveDiscoveryModel = model, IsConnected = true
        };
        var lease = new IedBrowserRcbCheckLease(device);
        Assert.True(lease.IsCurrent(device));

        // Even with a continuously selected and Connected UI card the new
        // association means an old client's RptEna/Resv cannot be "current".
        device.LiveValues.BeginAssociation();
        Assert.True(device.IsConnected);
        Assert.False(lease.IsCurrent(device));
        var newLease = new IedBrowserRcbCheckLease(device);
        Assert.True(newLease.IsCurrent(device));
        device.LiveDiscoveryModel = Model(false);
        Assert.False(newLease.IsCurrent(device));
        var replaced = new IedBrowserRcbCheckLease(device);
        Assert.True(replaced.IsCurrent(device));
        var other = new Iec61850MonitorDevice
        {
            LiveDiscoveryModel = model, IsConnected = true
        };
        Assert.False(replaced.IsCurrent(other));
        device.IsConnected = false;
        Assert.False(replaced.IsCurrent(device));
        device.IsConnected = true;
        Assert.False(replaced.IsCurrent(device));
    }

    [Fact]
    public void ConflictingFcdAndFcdaCannotAuthorizeEitherReportOrAnalogRead()
    {
        var model = Model();
        var schema = new IedBrowserSchemaIndex(model);
        var disputed = Owner + ".phsA.cVal.mag.f";
        var uncontested = Owner + ".phsB.cVal.mag.f";
        Assert.False(schema.TryResolveReportFc(DataSet, disputed, out _));
        Assert.False(schema.ContainsDataSetMember(DataSet, disputed, "MX"));
        Assert.False(schema.ContainsDataSetMember(DataSet, disputed, "ST"));
        Assert.True(schema.TryResolveReportFc(DataSet, uncontested, out var fc));
        Assert.Equal("MX", fc);
        Assert.True(schema.ContainsDataSetMember(DataSet, uncontested, "mx"));
        // FCD legitimately covers all of its declared descendants;
        // a sibling DO (not a descendant) must never match the prefix.
        Assert.False(schema.ContainsDataSetMember(DataSet,
            uncontested.Replace(".PhV.", ".PhVx.", StringComparison.Ordinal), "MX"));

        var page = new IedBrowserTreeRow("datasets/set/" + DataSet,
            "Phasors", "DataSet", 3, model.DataSets[0]);
        var targets = IedBrowserReadPlan.Build(model, page, readAll: false);
        Assert.Equal(2, targets.Count);
        Assert.DoesNotContain(targets, target =>
            IedBrowserLiveValues.CanonicalReference(target.Reference) == disputed);
        Assert.Contains(targets, target =>
            IedBrowserLiveValues.CanonicalReference(target.Reference) == uncontested);
        Assert.All(targets, t => Assert.Equal("MX", t.FunctionalConstraint));
    }

    [Fact]
    public void PureMxFcdKeepsAllPhasesAndSharesOneReadReportLedger()
    {
        var model = Model(false);
        var schema = new IedBrowserSchemaIndex(model);
        var member = model.DataSets[0].Members[0];
        var analog = IedBrowserStructuredDetails.Member(member, model);
        var dataObject = model.LogicalDevices[0].LogicalNodes[0].DataObjects[0];
        var direct = IedBrowserStructuredDetails.DataObject(dataObject, "MX");

        Assert.Equal(direct.PhasePlans, analog.PhasePlans);
        foreach (var phase in new[] { "phsA", "phsB", "phsC" })
        {
            var reference = Owner + "." + phase + ".cVal.mag.f";
            Assert.True(schema.TryResolveReportFc(DataSet, reference, out var fc));
            Assert.Equal("MX", fc);
            Assert.True(schema.ContainsDataSetMember(DataSet, reference, "MX"));
        }
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".phsA.cVal.mag.f", "MX", "0", "", "", "Report"));
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".phsB.cVal.mag.f", "MX", "10", "", "", "MMS Read"));
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            analog, ledger, new Dictionary<string, string?>(), true, out var fromSet));
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            direct, ledger, new Dictionary<string, string?>(), true, out var fromModel));
        Assert.Equal(fromModel.Text, fromSet.Text);
        Assert.Contains("phsA: 0", fromSet.Text);
        Assert.Contains("phsB: 10", fromSet.Text);
        Assert.Contains("phsC: not observed", fromSet.Text);
        Assert.DoesNotContain("∠ 0", fromSet.Text);
        Assert.False(ledger.TryGet(Owner + ".phsA.cVal.mag.f", "ST", out _));
        ledger.MarkOffline();
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            analog, ledger, new Dictionary<string, string?>(), true, out var stale));
        Assert.True(stale.IsStale);
    }

    [Fact]
    public void OccupiedRcbHasEvidenceGlyphButUnknownIsNotPaintedAsAvailable()
    {
        var row = new RcbExportRow { Reference = "LD/LLN0.RP.urcb01" };
        var node = new IedBrowserTreeRow("reports/rcb/" + row.Reference,
            row.Name, "RCB", 3, row);
        Assert.Equal("?", node.RcbUsageGlyph);
        Assert.Contains("unknown", node.RcbUsageTooltip, StringComparison.OrdinalIgnoreCase);
        row.Availability = MmsRcbOperationalAvailability.InUse;
        Assert.Equal("◆", node.RcbUsageGlyph);
        row.Availability = MmsRcbOperationalAvailability.UsedByCaller;
        Assert.Equal("●", node.RcbUsageGlyph);
        row.Availability = MmsRcbOperationalAvailability.Available;
        Assert.Equal("", node.RcbUsageGlyph);
    }

    [Fact]
    public void FcConflictsAreExplicitDiagnosticsButNotProcessEvidence()
    {
        var inconsistent = Model();
        var failures = IedBrowserDataSetFcConflictDiagnostic.Evaluate(
            inconsistent, maxSamples: 12);
        Assert.Equal(2, failures.CheckedMembers);
        var offending = Assert.Single(failures.Samples);
        Assert.Equal(1, failures.TotalCount);
        Assert.Equal(Owner + ".phsA.cVal.mag.f", offending.Reference);
        Assert.Equal("ST", offending.FunctionalConstraint);
        Assert.Equal(DataSet, offending.DataSet);
        Assert.Empty(IedBrowserDataSetFcConflictDiagnostic.Evaluate(
            Model(false)).Samples);
        Assert.Empty(IedBrowserDataSetFcConflictDiagnostic.Evaluate(
            inconsistent, maxSamples: 0).Samples);
        Assert.Equal(1, IedBrowserDataSetFcConflictDiagnostic.Evaluate(
            inconsistent, maxSamples: 0).TotalCount);
        Assert.Equal(0, IedBrowserDataSetFcConflictDiagnostic.Evaluate(null).TotalCount);

        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        Assert.False(ledger.TryGet(offending.Reference, "MX", out _));
    }

    [Fact]
    public void CopyDiagnosticsNamesConflictingModelDeclarationsWithoutMmsIo()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null &&
               !File.Exists(Path.Combine(root.FullName, "Services",
                   "DiagnosticReportBuilder.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var report = File.ReadAllText(Path.Combine(root!.FullName,
            "Services", "DiagnosticReportBuilder.cs"));
        Assert.Contains("IedBrowserDataSetFcConflictDiagnostic.Evaluate(", report);
        Assert.Contains("DataSet FC model conflicts:", report);
        Assert.Contains("inconsistent FCD/FCDA coverage", report);
    }

    [Fact]
    public void BrowserCheckValidatesAssociationBothAtCompletionAndAtRefresh()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null &&
               !File.Exists(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.IedBrowser.cs"));
        Assert.Contains("var checkLease = new IedBrowserRcbCheckLease(sourceDevice)", code);
        Assert.Contains("!checkLease.IsCurrent(sourceDevice)", code);
        Assert.Contains("_iedBrowserSnapshotLease?.IsCurrent(device) != true", code);
        Assert.Contains("_iedBrowserSnapshotLease = null;", code);
        Assert.DoesNotContain("new MmsClientSession(", code);
    }
}
