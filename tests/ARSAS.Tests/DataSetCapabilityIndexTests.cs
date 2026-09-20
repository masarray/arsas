using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DataSetCapabilityIndexTests
{
    [Fact]
    public void Fingerprints_AreSourceNeutralAndIgnoreCollectionOrdering()
    {
        var left = Model(
            dataSets: new[]
            {
                DataSet("IEDLD/LLN0.Digital", Member(0, "IEDLD/GGIO1.Ind1", "ST")),
                DataSet("IEDLD/LLN0.Analog", Member(0, "IEDLD/MMXU1.A.phsA", "MX"))
            },
            reports: new[]
            {
                Report("IEDLD/LLN0.BufDigital", "IEDLD/LLN0.Digital", buffered: true),
                Report("IEDLD/LLN0.BufAnalog", "IEDLD/LLN0.Analog", buffered: true)
            });

        var right = Model(
            dataSets: left.DataSets.Reverse().ToArray(),
            reports: left.ReportControls.Reverse().ToArray());

        var discovered = Iec61850DataSetCapabilityIndexBuilder.Build(left, 1, "SmartDiscovery");
        var opened = Iec61850DataSetCapabilityIndexBuilder.Build(right, 9, "OpenScl");

        Assert.Equal(discovered.DataSetFingerprint, opened.DataSetFingerprint);
        Assert.Equal(discovered.ReportBindingFingerprint, opened.ReportBindingFingerprint);
        Assert.Equal(discovered.ModelFingerprint, opened.ModelFingerprint);
        Assert.True(Iec61850DataSetCapabilityIndexBuilder.Compare(discovered, opened).IsEquivalent);
    }

    [Fact]
    public void DataSetFingerprint_PreservesCanonicalMemberIndexOrder()
    {
        var baseline = Model(
            dataSets: new[]
            {
                DataSet(
                    "IEDLD/LLN0.Analog",
                    Member(0, "IEDLD/MMXU1.A.phsA", "MX"),
                    Member(1, "IEDLD/MMXU1.A.phsB", "MX"))
            });

        var swapped = Model(
            dataSets: new[]
            {
                DataSet(
                    "IEDLD/LLN0.Analog",
                    Member(0, "IEDLD/MMXU1.A.phsB", "MX"),
                    Member(1, "IEDLD/MMXU1.A.phsA", "MX"))
            });

        var a = Iec61850DataSetCapabilityIndexBuilder.Build(baseline, 1, "SmartDiscovery");
        var b = Iec61850DataSetCapabilityIndexBuilder.Build(swapped, 1, "OpenScl");

        Assert.NotEqual(a.DataSetFingerprint, b.DataSetFingerprint);
        Assert.False(Iec61850DataSetCapabilityIndexBuilder.Compare(a, b).DataSetsMatch);
    }

    [Fact]
    public void ReportFingerprint_IgnoresMutableRuntimeRcbState()
    {
        var a = Model(
            reports: new[]
            {
                Report(
                    "IEDLD/LLN0.Buffer",
                    "IEDLD/LLN0.Analog",
                    buffered: true,
                    enabled: "false",
                    reservation: "free")
            });

        var b = Model(
            reports: new[]
            {
                Report(
                    "IEDLD/LLN0.Buffer",
                    "IEDLD/LLN0.Analog",
                    buffered: true,
                    enabled: "true",
                    reservation: "reserved")
            });

        var left = Iec61850DataSetCapabilityIndexBuilder.Build(a, 1, "SmartDiscovery");
        var right = Iec61850DataSetCapabilityIndexBuilder.Build(b, 2, "OpenScl");

        Assert.Equal(left.ReportBindingFingerprint, right.ReportBindingFingerprint);
    }

    [Fact]
    public void ReportFingerprint_ChangesWhenStableConfigurationChanges()
    {
        var a = Model(reports: new[]
        {
            Report("IEDLD/LLN0.Buffer", "IEDLD/LLN0.Analog", buffered: true, triggerOptions: "dchg")
        });
        var b = Model(reports: new[]
        {
            Report("IEDLD/LLN0.Buffer", "IEDLD/LLN0.Analog", buffered: true, triggerOptions: "dchg qchg")
        });

        var left = Iec61850DataSetCapabilityIndexBuilder.Build(a, 1, "SmartDiscovery");
        var right = Iec61850DataSetCapabilityIndexBuilder.Build(b, 1, "OpenScl");

        Assert.NotEqual(left.ReportBindingFingerprint, right.ReportBindingFingerprint);
        Assert.False(Iec61850DataSetCapabilityIndexBuilder.Compare(left, right).ReportBindingsMatch);
    }

    [Fact]
    public void Device_RejectsCapabilityWorkerResultFromOlderModelGeneration()
    {
        var device = new Iec61850MonitorDevice();
        var first = Model(dataSets: new[] { DataSet("IEDLD/LLN0.First", Member(0, "IEDLD/GGIO1.Ind1", "ST")) });
        device.LiveDiscoveryModel = first;
        var firstGeneration = device.ModelGeneration;
        var stale = Iec61850DataSetCapabilityIndexBuilder.Build(first, firstGeneration, "SmartDiscovery");

        device.LiveDiscoveryModel = Model(dataSets: new[] { DataSet("IEDLD/LLN0.Second", Member(0, "IEDLD/GGIO1.Ind2", "ST")) });

        Assert.True(device.ModelGeneration > firstGeneration);
        Assert.False(device.TryApplyDataSetCapabilityIndex(stale));
        Assert.Null(device.DataSetCapabilityIndex);
    }

    [Fact]
    public void PreparedIndex_ReportsDataSetAndConfiguredRcbCapability()
    {
        var model = Model(
            dataSets: new[]
            {
                DataSet(
                    "IEDLD/LLN0.Analog",
                    Member(0, "IEDLD/MMXU1.A.phsA", "MX"),
                    Member(1, "IEDLD/MMXU1.A.phsB", "MX")),
                DataSet(
                    "IEDLD/LLN0.Unreported",
                    Member(0, "IEDLD/GGIO1.Ind1", "ST"))
            },
            reports: new[]
            {
                Report("IEDLD/LLN0.Buffer", "IEDLD/LLN0.Analog", buffered: true)
            });

        var index = Iec61850DataSetCapabilityIndexBuilder.Build(model, 7, "SmartDiscovery");

        Assert.Equal(2, index.DataSetCount);
        Assert.Equal(3, index.MemberCount);
        Assert.Equal(1, index.ReportReadyDataSetCount);
        Assert.True(index.HasDataSets);
        Assert.Single(index.DataSets.Where(dataSet => dataSet.HasConfiguredReportControl));
    }

    private static LiveIedModelDiscoveryDocument Model(
        IReadOnlyList<LiveIedDataSetModel>? dataSets = null,
        IReadOnlyList<LiveIedReportControlModel>? reports = null)
        => new()
        {
            SchemaVersion = "live-ied-model-v1",
            IedName = "IED1",
            AccessPointName = "AP1",
            Source = "test",
            DataSets = dataSets ?? Array.Empty<LiveIedDataSetModel>(),
            ReportControls = reports ?? Array.Empty<LiveIedReportControlModel>()
        };

    private static LiveIedDataSetModel DataSet(
        string reference,
        params LiveIedDataSetMemberModel[] members)
        => new()
        {
            Reference = reference,
            Domain = reference.Split('/')[0],
            LogicalNode = "LLN0",
            Name = reference.Split('.').Last(),
            MemberCount = members.Length,
            Members = members
        };

    private static LiveIedDataSetMemberModel Member(
        int index,
        string reference,
        string functionalConstraint)
        => new()
        {
            Index = index,
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            MmsReference = reference.Replace('.', '$')
        };

    private static LiveIedReportControlModel Report(
        string reference,
        string dataSetReference,
        bool buffered,
        string triggerOptions = "dchg qchg",
        string enabled = "",
        string reservation = "")
        => new()
        {
            Reference = reference,
            Domain = reference.Split('/')[0],
            LogicalNode = "LLN0",
            Name = reference.Split('.').Last(),
            Buffered = buffered,
            Indexed = true,
            DataSetReference = dataSetReference,
            ReportId = "RID",
            ConfRev = "1",
            TriggerOptions = triggerOptions,
            OptionalFields = "seqNum timeStamp dataSet",
            BufferTimeMs = "0",
            IntegrityPeriodMs = "1000",
            EnabledState = enabled,
            ReservationState = reservation
        };
}
