using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Explicitly synthetic SG evidence distribution. The historical 23/12/45
/// counts are not a replay of 12 unknown field AccessResult responses.
/// </summary>
public sealed class P0CSgcbReadStateAndSsotTests
{
    private const string Domain = "BCUGEF650";
    private static readonly string[] Names = Enumerable.Range(0, 80)
        .Select(i => i switch
        {
            0 => "PTOCEna", 1 => "StrVal", 23 => "InMagTyp",
            24 => "RsMod", 25 => "OpDlTmms", 26 => "RsDlTmms",
            _ => "Attribute" + i.ToString("D2")
        }).ToArray();

    [Fact]
    public void Eighty_Sgcb_Targets_Keep_Observed_Failed_And_Deferred_Independent()
    {
        var (model, page) = Model();
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var all = IedBrowserReadPlan.Build(model, page, readAll: false).ToArray();
        Assert.Equal(80, all.Length);

        foreach (var target in all.Take(23))
        {
            var value = target.Reference.Contains("PTOCEna", StringComparison.Ordinal)
                ? "false" : target.Reference.Contains("StrVal", StringComparison.Ordinal)
                    ? "0" : "42";
            Assert.True(ledger.PublishForAssociation(association, target.Reference, "SG",
                value, "", "", "MMS Read"));
        }

        IedBrowserReadFailureSample? Absence(IedBrowserReadTarget target)
        {
            var ordinal = Array.FindIndex(Names, name =>
                target.Reference.Contains("$" + name + "$", StringComparison.Ordinal));
            return ordinal switch
            {
                >= 23 and < 35 => new IedBrowserReadFailureSample(
                    target.Reference, "SG", "Read attempted; accessResult/invoke NOT_CAPTURED",
                    IedBrowserReadAbsenceKind.Failed),
                >= 35 => new IedBrowserReadFailureSample(
                    target.Reference, "SG", "Deferred by bounded selected-read budget",
                    IedBrowserReadAbsenceKind.Deferred),
                _ => null
            };
        }

        var csv = IedBrowserReadEvidenceCsv.Build(model, page, ledger, Absence);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(81, lines.Length);
        Assert.Equal(23, lines.Count(s => s.Contains("\"OBSERVED\"", StringComparison.Ordinal)));
        Assert.Equal(12, lines.Count(s => s.Contains("\"READ_FAILED\"", StringComparison.Ordinal)));
        Assert.Equal(45, lines.Count(s => s.Contains("\"DEFERRED\"", StringComparison.Ordinal)));
        Assert.Equal(12, lines.Count(s => s.Contains(
            "\"UNRESOLVED\"", StringComparison.Ordinal)));
        Assert.Contains("\"false\"", csv);
        Assert.Contains("\"0\"", csv);
        Assert.DoesNotContain("\"UNKNOWN_ENUM_ZERO\"", csv);
        Assert.Contains("NOT_CAPTURED", csv);

        var schema = new IedBrowserSchemaIndex(model);
        var observed = Domain + "/LLN0.PTOCEna";
        Assert.True(ledger.TryGet(observed, "SG", out var parent, schema: schema));
        Assert.Equal("false", parent.Value);
        Assert.False(ledger.TryGet(observed, "SP", out _, schema: schema));
        Assert.False(ledger.TryGet(Domain + "/LLN0.InMagTyp", "SG",
            out _, schema: schema));

        ledger.BeginAssociation();
        Assert.False(ledger.TryGet(observed, "SG", out _, schema: schema));
        var stale = IedBrowserReadEvidenceCsv.Build(model, page, ledger);
        Assert.Equal(80, stale.Split('\n').Count(s => s.Contains(
            "\"NOT_OBSERVED_NO_ATTEMPT_EVIDENCE\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void Only_Exact_Observed_Access_Result_Number_Classifies_Failure()
    {
        Assert.Equal("UNRESOLVED", IedBrowserReadEvidenceProjector.FailureReasonCode(
            "INT32 failed; wire response not captured"));
        Assert.Equal("ACCESS_DENIED", IedBrowserReadEvidenceProjector.FailureReasonCode(
            "MMS Read AccessResult.failure code 3."));
        Assert.Equal("TYPE_MISMATCH", IedBrowserReadEvidenceProjector.FailureReasonCode(
            "MMS Read AccessResult.failure code 7."));
        Assert.Equal("NOT_SUPPORTED", IedBrowserReadEvidenceProjector.FailureReasonCode(
            "MMS Read AccessResult.failure code 9."));
        Assert.Equal("UNRESOLVED", IedBrowserReadEvidenceProjector.FailureReasonCode(
            "AccessResult.failure code seven"));
    }

    [Fact]
    public void Gva_Signed_Unsigned_And_Ordinal_Schema_Mismatch_Cannot_Project()
    {
        var root = Domain + "/LLN0$SG0";
        var target = new IedBrowserReadTarget(
            Domain + "/LLN0$SG$InMagTyp$setVal", "SG", "INT32");
        MmsTypeSpecificationNode Node(string type) => new()
        {
            MmsType = "structure",
            Children = [new MmsTypeSpecificationNode
            {
                Name = "InMagTyp", MmsType = "structure",
                Children = [new MmsTypeSpecificationNode
                { Name = "setVal", MmsType = type }]
            }]
        };
        MmsDataValue Wire(MmsDataValue child)
            => MmsDataValue.Structure([MmsDataValue.Structure([child])]);
        Assert.Empty(IedBrowserSgRootProjection.Project(root,
            Node("unsigned"), Wire(MmsDataValue.Integer(-1)), [target]));
        Assert.Empty(IedBrowserSgRootProjection.Project(root,
            Node("integer"), Wire(MmsDataValue.Unsigned(1)), [target]));
        Assert.Empty(IedBrowserSgRootProjection.Project(root,
            Node("integer"), MmsDataValue.Structure([]), [target]));
        Assert.Equal("-1", Assert.Single(IedBrowserSgRootProjection.Project(root,
            Node("integer"), Wire(MmsDataValue.Integer(-1)), [target])).Value);
    }

    private static (LiveIedModelDiscoveryDocument Model, IedBrowserTreeRow Page) Model()
    {
        var dataObjects = Names.Select(name => new LiveIedDataObjectModel
        {
            Name = name,
            Reference = Domain + "/LLN0." + name,
            Attributes = [new LiveIedDataAttributeModel
            {
                ObjectReference = Domain + "/LLN0." + name,
                AttributePath = "setVal",
                MmsReference = Domain + "/LLN0$SG$" + name + "$setVal",
                FunctionalConstraint = "SG",
                SclBType = name == "PTOCEna" ? "BOOLEAN" : "INT32",
                TypeDiscoveryStatus = "Exact"
            }]
        }).ToArray();
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = Domain,
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "LLN0", DataObjects = dataObjects
                }]
            }]
        };
        var page = new IedBrowserTreeRow("sgcb", "Setting Groups", "SGCB", 2,
            new LiveIedControlBlockModel
            { Kind = "SettingGroupControl", Domain = Domain, LogicalNode = "LLN0" });
        return (model, page);
    }
}
