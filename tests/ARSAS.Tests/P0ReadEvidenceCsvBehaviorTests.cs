using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0ReadEvidenceCsvBehaviorTests
{
    private const string Domain = "BCUGEF650";
    
    [Fact]
    public void EmptyBrowserCategory_ReturnsExplicitZeroTargets_NeverSuccessCsv()
    {
        var model = new LiveIedModelDiscoveryDocument();
        var category = new IedBrowserTreeRow("category/reports", "Reports", "Category", 1);
        var ledger = new IedBrowserLiveValues();
        Assert.Empty(IedBrowserReadEvidenceCsv.Plan(model, category));
        var problem = Assert.Throws<InvalidOperationException>(() =>
            IedBrowserReadEvidenceCsv.Build(model, category, ledger));
        Assert.Contains("ZERO_TARGETS", problem.Message);
        Assert.Throws<InvalidOperationException>(() =>
            IedBrowserReadEvidenceCsv.BuildSelection([], ledger));
        Assert.Empty(IedBrowserReadEvidenceCsv.Plan(null, category));
        Assert.Empty(IedBrowserReadEvidenceCsv.Plan(model, category, maxTargets: 0));
    }

    [Fact]
    public void ScopedReadEvidence_UsesIdenticalTargetSetForPreflightAndExport()
    {
        var attr = Attribute("PIOCEna");
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = Domain,
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "LLN0",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "PIOCEna", Reference = Domain + "/LLN0.PIOCEna",
                        Attributes = [attr]
                    }]
                }]
            }]
        };
        var page = new IedBrowserTreeRow("model/ld/LLN0", "LLN0", "LN", 2,
            model.LogicalDevices[0].LogicalNodes[0]);
        var targets = IedBrowserReadEvidenceCsv.Plan(model, page);
        Assert.Single(targets);
        var ledger = new IedBrowserLiveValues();
        var csv = IedBrowserReadEvidenceCsv.BuildSelection(targets, ledger);
        var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(targets.Count + 1, rows.Length);
        Assert.Contains("\"NOT_OBSERVED_NO_ATTEMPT_EVIDENCE\"", csv);
        Assert.Equal(csv, IedBrowserReadEvidenceCsv.Build(model, page, ledger));
    }

    private static LiveIedDataAttributeModel Attribute(string name)
        => new()
        {
            ObjectReference = Domain + "/LLN0." + name,
            AttributePath = name + ".setVal",
            MmsReference = Domain + "/LLN0$SG$" + name + "$setVal",
            FunctionalConstraint = "SG", SclBType = "BOOLEAN",
            TypeDiscoveryStatus = "Exact"
        };

    [Fact]
    public void CsvSeparatesCanonicalObservedMmsReadFromExactFailureAndUninstrumentedWire()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = Domain,
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "LLN0", DataObjects =
                    [
                        new LiveIedDataObjectModel
                        {
                            Name = "PIOCEna", Reference = Domain + "/LLN0.PIOCEna",
                            Attributes = [Attribute("PIOCEna")]
                        },
                        new LiveIedDataObjectModel
                        {
                            Name = "InMagTyp", Reference = Domain + "/LLN0.InMagTyp",
                            Attributes = [Attribute("InMagTyp")]
                        }
                    ]
                }]
            }]
        };
        var page = new IedBrowserTreeRow("sgcb/LD", "SGCB", "SGCB", 2,
            new LiveIedControlBlockModel
            { Kind = "SettingGroupControl", Domain = Domain, LogicalNode = "LLN0" });
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(association,
            Domain + "/LLN0.PIOCEna.setVal", "SG", "false", "", "", "MMS Read",
            readWireShapeEvidence: "wireKind=Boolean, category=primitive, clrKind=Boolean, rawOctets=1, childCount=0"));
        var csv = IedBrowserReadEvidenceCsv.Build(model, page, ledger,
            target => target.Reference.Contains("$InMagTyp$", StringComparison.Ordinal)
                ? new IedBrowserReadFailureSample(target.Reference, "SG",
                    "typed root mismatch, refused", IedBrowserReadAbsenceKind.Failed)
                : null);
        Assert.Contains("\"BCUGEF650/LLN0.PIOCEna.setVal\",\"SG\"", csv);
        Assert.Contains("\"OBSERVED\"", csv);
        Assert.Contains("DecodedMmsWireShape", csv);
        Assert.Contains("wireKind=Boolean, category=primitive", csv);
        // We have decoder evidence, NOT invoke ID or GVA proof. Keeping
        // those separate prevents synthetic wire-fidelity conclusions.
        Assert.Contains("\"NOT_CAPTURED\",\"NOT_CAPTURED\"", csv);

        Assert.Contains("\"MMS Read\"", csv);
        Assert.Contains("\"READ_FAILED\"", csv);
        Assert.Contains("\"typed root mismatch, refused\"", csv);
        Assert.Contains("\"NOT_CAPTURED\",\"NOT_CAPTURED\",\"NOT_MEASURED\"", csv);
        Assert.DoesNotContain("\"phasor\"", csv);

        ledger.BeginAssociation();
        var stale = IedBrowserReadEvidenceCsv.Build(model, page, ledger);
        Assert.DoesNotContain("\"OBSERVED\"", stale);
        Assert.Contains("\"NOT_OBSERVED_NO_ATTEMPT_EVIDENCE\"", stale);
    }
}
