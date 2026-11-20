using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class QualityInvalidAttentionTests
{
    [Theory]
    [InlineData("Invalid", true)]
    [InlineData("invalid", true)]
    [InlineData(" INVALID ", true)]
    [InlineData("Invalid / Overflow", true)]
    [InlineData("Invalid (Failure)", true)]
    [InlineData("Good", false)]
    [InlineData("Questionable", false)]
    [InlineData("Reserved", false)]
    [InlineData("Pending / q not supplied", false)]
    [InlineData("Unknown", false)]
    [InlineData("Invalidity", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void InvalidValidity_IsStrictlyIdentifiedForUiOnly(string? quality, bool expected)
        => Assert.Equal(expected, QualityInvalidAttention.IsInvalid(quality));

    [Fact]
    public void LivePoint_UpdatesAttentionAndPreservesRawQuality()
    {
        var point = new Iec61850MonitorPoint();
        var notifications = new List<string?>();
        point.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        Assert.False(point.IsInvalidQuality);
        point.Quality = "Invalid / Overflow";
        Assert.True(point.IsInvalidQuality);
        Assert.Equal("Invalid / Overflow", point.Quality);
        Assert.Contains(nameof(point.IsInvalidQuality), notifications);
        point.Quality = "Good";
        Assert.False(point.IsInvalidQuality);
    }

    [Fact]
    public void Browser_WarnsOnlyForQualityValidityAndExactQualityLeaves()
    {
        var root = new IedBrowserDetailRow("Data", "q [ST]", "Not read")
        {
            EvidenceKind = "Quality",
            FunctionalConstraint = "ST",
            ValueReference = "LD/PTOC1.Op.q"
        };
        var changes = new List<string?>();
        root.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        root.Value = "Invalid";
        Assert.True(root.IsInvalidQuality);
        Assert.Equal("Invalid", root.Value);
        Assert.Contains(nameof(root.IsInvalidQuality), changes);

        var validity = new IedBrowserDetailRow("Data", "Validity", "invalid")
        {
            EvidenceKind = "Quality", EvidenceField = "Validity"
        };
        Assert.True(validity.IsInvalidQuality);
        var otherFlag = new IedBrowserDetailRow("Data", "Failure", "invalid")
        {
            EvidenceKind = "Quality", EvidenceField = "Failure"
        };
        Assert.False(otherFlag.IsInvalidQuality);
        var processValue = new IedBrowserDetailRow("Data", "general", "Invalid");
        Assert.False(processValue.IsInvalidQuality);
        var dataSetLeaf = new IedBrowserDetailRow("Data", "q [ST]", "invalid")
        {
            FunctionalConstraint = "ST", ValueReference = "LD/CSWI1.Pos.q"
        };
        Assert.True(dataSetLeaf.IsInvalidQuality);
        root.Value = "Good";
        Assert.False(root.IsInvalidQuality);
    }
}
