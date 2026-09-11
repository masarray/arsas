using System.Globalization;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeCursorReadoutPolicyTests
{
    [Fact]
    public void IsCurrent_AcceptsOnlyLatestNonCancelledRevision()
    {
        Assert.True(ComtradeCursorReadoutPolicy.IsCurrent(12, 12, cancelled: false));
        Assert.False(ComtradeCursorReadoutPolicy.IsCurrent(11, 12, cancelled: false));
        Assert.False(ComtradeCursorReadoutPolicy.IsCurrent(12, 12, cancelled: true));
    }

    [Fact]
    public void FormatValue_NeverReturnsBlankForUnavailableMeasurement()
    {
        Assert.Equal("C1 Inst —", ComtradeCursorReadoutPolicy.FormatValue("C1", rms: false, null, CultureInfo.InvariantCulture));
        Assert.Equal("C2 RMS —", ComtradeCursorReadoutPolicy.FormatValue("C2", rms: true, double.NaN, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatValue_FormatsFiniteLatestValueDeterministically()
    {
        Assert.Equal("C1 Inst -1.23457", ComtradeCursorReadoutPolicy.FormatValue("C1", rms: false, -1.2345678, CultureInfo.InvariantCulture));
        Assert.Equal("C2 RMS 70.125", ComtradeCursorReadoutPolicy.FormatValue("C2", rms: true, 70.125, CultureInfo.InvariantCulture));
    }
}