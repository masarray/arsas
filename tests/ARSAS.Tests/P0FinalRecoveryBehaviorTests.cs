using System.Reflection;
using System.Xml.Linq;
using ArIED61850Tester;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using Xunit;

namespace ARSAS.Tests;

public sealed class P0FinalRecoveryBehaviorTests
{
    [Fact]
    public void FatDisposition_DoesNotRewriteOperatorTestCheckbox()
    {
        var point = new IoTestPointPlan
        {
            TestPointId = "P0-TEST-AUTHORITY",
            IedName = "IED-A",
            IpAddress = "192.0.2.10",
            SignalName = "Trip",
            ObjectReference = "IED-ALD0/GGIO1.Ind1.stVal",
            FunctionalConstraint = "ST",
            ExpectedOnText = "True",
            ExpectedOffText = "False",
            TestEnabled = true,
            WorkspaceSelected = true,
            ImportReady = true
        };

        Assert.True(point.TestEnabled);
        Assert.True(point.IsIncludedInFat);

        point.RemoveFromFat();
        Assert.False(point.IsIncludedInFat);
        Assert.True(point.TestEnabled);

        point.RestoreToFat();
        Assert.True(point.IsIncludedInFat);
        Assert.True(point.TestEnabled);

        point.TestEnabled = false;
        point.RemoveFromFat();
        point.RestoreToFat();
        Assert.False(point.TestEnabled);
    }

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(11, 10, true)]
    [InlineData(9, 10, false)]
    public void LiveBeforeEvidence_RequiresVisibleSequenceAtOrBeyondEvidence(
        long liveVisibleSequence,
        long evidenceProcessSequence,
        bool expected)
    {
        var method = typeof(MainWindow).GetMethod(
            "CanPublishEvidenceForTest",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainWindow).FullName, "CanPublishEvidenceForTest");

        var actual = Assert.IsType<bool>(method.Invoke(
            null,
            new object?[] { liveVisibleSequence, evidenceProcessSequence }));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GenericSclContainment_RejectsArsasRuntimeDataSetButAcceptsNativeDataSet()
    {
        var validator = typeof(MainWindow).GetMethod(
            "ValidateGenericMultiRcbDocument",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainWindow).FullName, "ValidateGenericMultiRcbDocument");

        static XDocument Document(string dataSetName)
            => XDocument.Parse($"<SCL><IED name='IED-A'><AccessPoint name='P1'><Server><LDevice inst='LD0'><LN0 lnClass='LLN0' inst=''><DataSet name='{dataSetName}' /></LN0></LDevice></Server></AccessPoint></IED></SCL>");

        validator.Invoke(null, new object?[] { Document("ProtectionEvents"), 0 });

        var failure = Assert.Throws<TargetInvocationException>(() =>
            validator.Invoke(null, new object?[] { Document("ARIED_7A1B2C3D"), 0 }));
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Contains("runtime DataSet", failure.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RcbSelection_NormalClicksRemainIndependent()
    {
        var rows = Rows(5);
        var anchorIndex = -1;
        var anchorValue = false;

        Apply(rows, ref anchorIndex, ref anchorValue, 0, extendRange: false);
        Apply(rows, ref anchorIndex, ref anchorValue, 2, extendRange: false);
        Apply(rows, ref anchorIndex, ref anchorValue, 4, extendRange: false);

        Assert.True(rows[0].IsSelected);
        Assert.False(rows[1].IsSelected);
        Assert.True(rows[2].IsSelected);
        Assert.False(rows[3].IsSelected);
        Assert.True(rows[4].IsSelected);
    }

    [Fact]
    public void RcbSelection_ShiftRangeUsesAnchorInclusionState()
    {
        var rows = Rows(6);
        var anchorIndex = -1;
        var anchorValue = false;

        Apply(rows, ref anchorIndex, ref anchorValue, 1, extendRange: false);
        Assert.True(rows[1].IsSelected);

        Apply(rows, ref anchorIndex, ref anchorValue, 4, extendRange: true);
        Assert.False(rows[0].IsSelected);
        Assert.All(rows.Skip(1).Take(4), row => Assert.True(row.IsSelected));
        Assert.False(rows[5].IsSelected);

        // A normal toggle creates a new OFF anchor. Shift then applies OFF to the block
        // instead of inverting every row independently.
        Apply(rows, ref anchorIndex, ref anchorValue, 3, extendRange: false);
        Assert.False(rows[3].IsSelected);
        Apply(rows, ref anchorIndex, ref anchorValue, 0, extendRange: true);

        Assert.All(rows.Take(4), row => Assert.False(row.IsSelected));
        Assert.True(rows[4].IsSelected);
        Assert.False(rows[5].IsSelected);
    }

    private static List<RcbExportRow> Rows(int count)
        => Enumerable.Range(1, count)
            .Select(index => new RcbExportRow
            {
                Name = $"RCB-{index}",
                Reference = $"IEDLD0/LLN0.RP.RCB{index}",
                Type = "Buffered"
            })
            .ToList();

    private static void Apply(
        IList<RcbExportRow> rows,
        ref int anchorIndex,
        ref bool anchorValue,
        int targetIndex,
        bool extendRange)
    {
        var method = typeof(MainWindow).GetMethod(
            "ApplyRcbSelectionForTest",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainWindow).FullName, "ApplyRcbSelectionForTest");

        object?[] arguments = { rows, anchorIndex, anchorValue, targetIndex, extendRange };
        method.Invoke(null, arguments);
        anchorIndex = Assert.IsType<int>(arguments[1]);
        anchorValue = Assert.IsType<bool>(arguments[2]);
    }
}
