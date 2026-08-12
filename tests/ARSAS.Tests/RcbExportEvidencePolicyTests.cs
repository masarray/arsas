using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class RcbExportEvidencePolicyTests
{
    [Fact]
    public void E016Style_ConfiguredCtrlUrcb_IsNeverNoDataSetBeforeLiveCheck()
    {
        var availability = RcbExportEvidencePolicy.SourceAvailability(
            liveAvailability: null,
            configuredDataSetName: "DataSet",
            dataSetResolved: true,
            configuredMemberCount: 31);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, availability);
        Assert.NotEqual(MmsRcbOperationalAvailability.NoDataSet, availability);
    }

    [Fact]
    public void E016Style_UnboundExtUrcb_IsNoDataSetFromPositiveSclEvidence()
    {
        var availability = RcbExportEvidencePolicy.SourceAvailability(
            liveAvailability: null,
            configuredDataSetName: string.Empty,
            dataSetResolved: false,
            configuredMemberCount: 0);

        Assert.Equal(MmsRcbOperationalAvailability.NoDataSet, availability);
    }

    [Fact]
    public void LiveModel_BlankBindingWithoutSnapshot_RemainsUnknown()
    {
        var availability = RcbExportEvidencePolicy.LiveModelAvailability(
            liveAvailability: null,
            effectiveDataSetReference: string.Empty,
            dataSetResolved: false,
            memberCount: 0);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, availability);
        Assert.NotEqual(MmsRcbOperationalAvailability.NoDataSet, availability);
    }

    [Fact]
    public void LiveVerifiedNoDataSet_RemainsAuthoritative()
    {
        var availability = RcbExportEvidencePolicy.LiveModelAvailability(
            liveAvailability: MmsRcbOperationalAvailability.NoDataSet,
            effectiveDataSetReference: string.Empty,
            dataSetResolved: false,
            memberCount: 0);

        Assert.Equal(MmsRcbOperationalAvailability.NoDataSet, availability);
    }

    [Fact]
    public void LiveSnapshotReference_OverridesStaleDiscoveryReference()
    {
        var effective = RcbExportEvidencePolicy.EffectiveDataSetReference(
            "E016MD66CTRL/LLN0.DataSet",
            string.Empty);

        Assert.Equal("E016MD66CTRL/LLN0.DataSet", effective);
    }

    [Fact]
    public void DuplicateShortRcbNames_AreDistinguishedByLogicalScope()
    {
        var ctrl = RcbExportEvidencePolicy.ScopeFromReference("E016MD66CTRL/LLN0.RP.urcbA01");
        var ext = RcbExportEvidencePolicy.ScopeFromReference("E016MD66EXT/LLN0.RP.urcbA01");
        var meas = RcbExportEvidencePolicy.ScopeFromReference("E016MD66MEAS/LLN0.RP.urcbA01");

        Assert.Equal("E016MD66CTRL / LLN0", ctrl);
        Assert.Equal("E016MD66EXT / LLN0", ext);
        Assert.Equal("E016MD66MEAS / LLN0", meas);
        Assert.Equal(3, new[] { ctrl, ext, meas }.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EvidenceConflict_BlocksSelectionEvenWhenMembersExist()
    {
        var row = new RcbExportRow
        {
            Name = "urcbA01",
            Reference = "E016MD66CTRL/LLN0.RP.urcbA01",
            ScopeText = "CTRL / LLN0",
            DataSetName = "DataSet",
            DataSetReference = "E016MD66CTRL/LLN0.DataSet",
            MemberCount = 31,
            Availability = MmsRcbOperationalAvailability.Available,
            HasEvidenceConflict = true
        };

        Assert.False(row.IsSelectable);
    }

    [Fact]
    public void BrokenConfiguredReference_IsUnknownNotNoDataSet()
    {
        var availability = RcbExportEvidencePolicy.SourceAvailability(
            liveAvailability: null,
            configuredDataSetName: "MissingDataSet",
            dataSetResolved: false,
            configuredMemberCount: 0);

        Assert.Equal(MmsRcbOperationalAvailability.Unknown, availability);
        Assert.NotEqual(MmsRcbOperationalAvailability.NoDataSet, availability);
    }
}
