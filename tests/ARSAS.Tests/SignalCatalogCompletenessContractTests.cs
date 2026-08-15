using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Signal Selection completeness contract.
///
/// Static IEC 61850 DataSet membership is authoritative inventory evidence. Presentation
/// policies may classify or sort those rows, but must never silently drop them merely
/// because a primary value leaf, CDC semantic, or familiar LN/DO pattern is unresolved.
/// </summary>
public sealed class SignalCatalogCompletenessContractTests
{
    [Fact]
    public void OpenScl_Static_DataSet_Membership_Is_NonDroppable_Even_For_Unknown_Vendor_Objects()
    {
        var members = new[]
        {
            Member(0, "IEDLD0/GGIO6.CBOpnd", "ST"),
            Member(1, "IEDLD0/GGIO6.CBClsd", "ST"),
            Member(2, "IEDLD0/GGIO2.TCS1Fail", "ST"),
            Member(3, "IEDLD0/GGIO2.ComFail", "ST"),
            Member(4, "IEDLD0/GGIO2.FWUpdated", "ST"),
            Member(5, "IEDLD0/GGIO1.LocOpnCMDsta", "ST"),
            Member(6, "IEDLD0/VEND1.CustomInterlock", "ST"),
            Member(7, "IEDLD0/VEND1.CustomAlarm", "ST"),
            Member(8, "IEDLD0/MMXU1.CustomMagnitude", "MX")
        };
        var model = ModelWithDataSet("IEDLD0/LLN0.ImportantSignals", members);
        var workspace = new SclIedWorkspace
        {
            IedName = "IED",
            AccessPointName = "P1",
            DesignModel = model
        };

        var signals = SclWorkspaceSignalMapper.BuildSignals(workspace);

        AssertAllDataSetMembersRepresented(model, signals);
        Assert.All(signals, signal => Assert.False(signal.IsSelected));
        Assert.Contains(signals, signal => signal.ObjectReference == "IEDLD0/VEND1.CustomInterlock");
        Assert.Contains(signals, signal => signal.ObjectReference == "IEDLD0/GGIO2.TCS1Fail");
    }

    [Fact]
    public void OpenScl_SiemensLike_Fcd_Inventory_Is_Conserved_58_Of_58()
    {
        var digital = Enumerable.Range(1, 36)
            .Select(index => Member(index - 1, $"IEDLD0/GGIO1.Dig{index:00}", "ST"))
            .ToArray();
        var analog = Enumerable.Range(1, 22)
            .Select(index => Member(index - 1, $"IEDLD0/MMXU1.Ana{index:00}", "MX"))
            .ToArray();
        var model = ModelWithDataSets(
            ("IEDLD0/LLN0.Digital", digital),
            ("IEDLD0/LLN0.Analog", analog));
        var workspace = new SclIedWorkspace
        {
            IedName = "IED",
            AccessPointName = "P1",
            DesignModel = model
        };

        var signals = SclWorkspaceSignalMapper.BuildSignals(workspace);

        Assert.Equal(58, model.DataSets.Sum(dataSet => dataSet.Members.Count));
        AssertAllDataSetMembersRepresented(model, signals);
    }

    [Fact]
    public void IpDiscovery_SiemensLike_Fcd_Inventory_Is_Conserved_58_Of_58()
    {
        var digital = Enumerable.Range(1, 36)
            .Select(index => Member(index - 1, $"IEDLD0/GGIO1.Dig{index:00}", "ST"))
            .ToArray();
        var analog = Enumerable.Range(1, 22)
            .Select(index => Member(index - 1, $"IEDLD0/MMXU1.Ana{index:00}", "MX"))
            .ToArray();
        var model = ModelWithDataSets(
            ("IEDLD0/LLN0.Digital", digital),
            ("IEDLD0/LLN0.Analog", analog));
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = model
        };

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        Assert.Equal(58, result.MandatoryCatalogCount);
        AssertAllDataSetMembersRepresented(model, device.Signals);
    }

    private static void AssertAllDataSetMembersRepresented(
        LiveIedModelDiscoveryDocument model,
        IEnumerable<SignalDefinition> signals)
    {
        var present = signals
            .Select(signal => Normalize(signal.ObjectReference))
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = model.DataSets
            .SelectMany(dataSet => dataSet.Members)
            .Select(member => Normalize(member.Reference))
            .Where(reference => reference.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var missing = expected
            .Where(reference => !present.Contains(reference))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Static DataSet members disappeared from Signal Selection: " + string.Join(", ", missing));
    }

    private static LiveIedModelDiscoveryDocument ModelWithDataSet(
        string dataSetReference,
        IReadOnlyList<LiveIedDataSetMemberModel> members)
        => ModelWithDataSets((dataSetReference, members));

    private static LiveIedModelDiscoveryDocument ModelWithDataSets(
        params (string Reference, IReadOnlyList<LiveIedDataSetMemberModel> Members)[] dataSets)
        => new()
        {
            Source = "SignalCatalogCompletenessContract",
            IedName = "IED",
            LogicalDevices = Array.Empty<LiveIedLogicalDeviceModel>(),
            DataSets = dataSets.Select(item =>
            {
                var slash = item.Reference.IndexOf('/');
                var domain = slash > 0 ? item.Reference[..slash] : string.Empty;
                var path = slash >= 0 ? item.Reference[(slash + 1)..] : item.Reference;
                var dot = path.IndexOf('.');
                var logicalNode = dot > 0 ? path[..dot] : string.Empty;
                var name = dot >= 0 && dot < path.Length - 1 ? path[(dot + 1)..] : path;
                return new LiveIedDataSetModel
                {
                    Reference = item.Reference,
                    Domain = domain,
                    LogicalNode = logicalNode,
                    Name = name,
                    MemberCount = item.Members.Count,
                    Members = item.Members
                };
            }).ToArray()
        };

    private static LiveIedDataSetMemberModel Member(int index, string reference, string functionalConstraint)
    {
        var slash = reference.IndexOf('/');
        var domain = reference[..slash];
        var path = reference[(slash + 1)..];
        var firstDot = path.IndexOf('.');
        var logicalNode = path[..firstDot];
        var objectPath = path[(firstDot + 1)..].Replace('.', '$');
        return new LiveIedDataSetMemberModel
        {
            Index = index,
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            MmsReference = $"{domain}/{logicalNode}${functionalConstraint}${objectPath}",
            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
    }

    private static string Normalize(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').ToUpperInvariant();
}
