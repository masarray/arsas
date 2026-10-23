using System.Reflection;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ReportBooleanDisplaySemanticEdgeTests
{
    [Theory]
    [InlineData("Boolean", "True", "True [1]")]
    [InlineData("Boolean", "True [1]", "True")]
    [InlineData("Boolean", "False", "False [0]")]
    [InlineData("Boolean", "False [0]", "False")]
    [InlineData("BOOL", "1", "True [1]")]
    [InlineData("SPS", "true", "True [1]")]
    [InlineData("SPC", "false", "False [0]")]
    [InlineData("SinglePointStatus", "False [0]", "0")]
    public void BooleanPresentationOnlyChange_DoesNotRaiseProcessEdge(
        string dataType, string previous, string next)
    {
        Assert.False(HasExactSemanticEdge(dataType, previous, next));
    }

    [Theory]
    [InlineData("Boolean", "True", "False [0]")]
    [InlineData("SPS", "False [0]", "True [1]")]
    [InlineData("BOOL", "True", "True [0]")]
    [InlineData("DPC", "Open [01]", "Closed [10]")]
    public void GenuineOrContradictoryStateChange_RemainsAnEdge(
        string dataType, string previous, string next)
    {
        Assert.True(HasExactSemanticEdge(dataType, previous, next));
    }

    [Fact]
    public void DoublePointFormatting_IsNotNormalizedAsBoolean()
    {
        Assert.False(HasExactSemanticEdge("DPC", "Open [01]", "OPEN [01]"));
        Assert.True(HasExactSemanticEdge("DPC", "Intermediate [00]", "Open [01]"));
    }

    [Fact]
    public void TestCoversTheActualOperatorReportFormatter()
    {
        var trueDisplay = Iec61850ValueFormatter.FormatReportProcessValue(
            true, "Boolean", string.Empty, "Status", "IED/LLN0.Test.stVal");
        var falseDisplay = Iec61850ValueFormatter.FormatReportProcessValue(
            false, "Boolean", string.Empty, "Status", "IED/LLN0.Test.stVal");

        Assert.Equal("True [1]", trueDisplay);
        Assert.Equal("False [0]", falseDisplay);
        Assert.False(HasExactSemanticEdge("Boolean", "True", trueDisplay));
        Assert.False(HasExactSemanticEdge("Boolean", "False", falseDisplay));
    }

    private static bool HasExactSemanticEdge(string dataType, string previous, string next)
    {
        var point = new Iec61850MonitorPoint
        {
            IecDataType = dataType,
            Category = "Status",
            IecReference = "IED/LLN0.Test.stVal"
        };
        var method = typeof(Iec61850MonitorRuntime).GetMethod(
            "HasExactSemanticEdge",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(null, new object[] { point, previous, next }));
    }
}
