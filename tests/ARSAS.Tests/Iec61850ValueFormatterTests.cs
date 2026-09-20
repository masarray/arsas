using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class Iec61850ValueFormatterTests
{
    [Theory]
    [InlineData("true", "True [1]")]
    [InlineData("false", "False [0]")]
    [InlineData(true, "True [1]")]
    [InlineData(false, "False [0]")]
    public void FormatReportProcessValue_RestoresBooleanOperatorText(object value, string expected)
    {
        var formatted = Iec61850ValueFormatter.FormatReportProcessValue(
            value,
            "Boolean",
            string.Empty,
            "Status",
            "IEDLD/GGIO1.SwLoc.stVal");

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("0", "False [0]")]
    [InlineData("1", "True [1]")]
    [InlineData(0, "False [0]")]
    [InlineData(1, "True [1]")]
    public void FormatReportProcessValue_UsesDeclaredBooleanTypeForNumericState(
        object value,
        string expected)
    {
        var formatted = Iec61850ValueFormatter.FormatReportProcessValue(
            value,
            "SPS",
            string.Empty,
            "Status",
            "IEDLD/GGIO1.Ind1.stVal");

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("off", "Open [01]")]
    [InlineData("on", "Close [10]")]
    [InlineData("intermediate-state", "Intermediate [00]")]
    [InlineData("bad-state", "Bad state [11]")]
    public void FormatReportProcessValue_RestoresDpcOperatorContext(string value, string expected)
    {
        var formatted = Iec61850ValueFormatter.FormatReportProcessValue(
            value,
            "Enum",
            string.Empty,
            "Position",
            "IEDLD/CSWI1.Pos.stVal");

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void FormatReportProcessValue_DoesNotInterpretGenericOnOffAsDpcOutsidePositionContext()
    {
        var formatted = Iec61850ValueFormatter.FormatReportProcessValue(
            "off",
            "Enum",
            string.Empty,
            "Status",
            "IEDLD/GGIO1.AutoMode.stVal");

        Assert.Equal("off", formatted);
    }

    [Fact]
    public void FormatReportProcessValue_LeavesNumericMeasurementUnchanged()
    {
        var formatted = Iec61850ValueFormatter.FormatReportProcessValue(
            "123.45",
            "Float32",
            "V",
            "Measurement",
            "IEDLD/MMXU1.PhV.phsA.cVal.mag.f");

        Assert.Equal("123.45 V", formatted);
    }

    [Fact]
    public void Format_Extracts_Boolean_StVal_From_Legacy_Report_Structure()
    {
        const string value = "Structure(3) {stVal=false, q=Quality{V=1,D=0,Ov=0,F=0,Osc=0,B=0,Oot=0,Incon=0,Ina=0,src=0,test=0,opBlk=0}, t=BinaryTime(2026-08-15 16:11:57.723, Q=0x0A, ext=True)}";

        var formatted = Iec61850ValueFormatter.Format(value, "Boolean", string.Empty);

        Assert.Equal("False", formatted);
    }

    [Fact]
    public void Format_Extracts_True_Boolean_StVal_From_Legacy_Report_Structure()
    {
        const string value = "Structure(3) {stVal=true, q=Quality{V=1,D=0}, t=BinaryTime(2026-08-15 16:11:57.723, Q=0x0A, ext=True)}";

        var formatted = Iec61850ValueFormatter.Format(value, "Boolean", string.Empty);

        Assert.Equal("True", formatted);
    }

    [Fact]
    public void Format_Extracts_Integer_StVal_From_Counter_Status_Structure()
    {
        const string value = "Structure(3) {stVal=0, q=Quality{V=1,D=0}, t=BinaryTime(2026-08-15 16:11:57.723, Q=0x0A, ext=True)}";

        var formatted = Iec61850ValueFormatter.Format(value, "Int32", string.Empty);

        Assert.Equal("0", formatted);
    }

    [Fact]
    public void Format_Extracts_Indexed_Numeric_ActVal_From_Bcr_Structure_When_Metadata_Is_Numeric()
    {
        const string value = "Structure(5) {[0]=12345, [1]=Quality{V=1,D=0}, [2]=BinaryTime(2026-08-15 16:11:57.723), [3]=false, [4]=BinaryTime(2026-08-15 16:11:00.000)}";

        var formatted = Iec61850ValueFormatter.Format(value, "Int64", "Wh");

        Assert.Equal("12345 Wh", formatted);
    }

    [Fact]
    public void Format_Does_Not_Collapse_Indexed_Structure_When_Metadata_Is_Not_Numeric()
    {
        const string value = "Structure(5) {[0]=12345, [1]=Quality{V=1,D=0}, [2]=BinaryTime(2026-08-15 16:11:57.723), [3]=false, [4]=BinaryTime(2026-08-15 16:11:00.000)}";

        var formatted = Iec61850ValueFormatter.Format(value, "Structure", string.Empty);

        Assert.Equal(value, formatted);
    }

    [Fact]
    public void Format_Does_Not_Collapse_NonStVal_Structures()
    {
        const string value = "Structure(3) {mag=123.4, q=Quality{V=1,D=0}, t=BinaryTime(2026-08-15 16:11:57.723, Q=0x0A, ext=True)}";

        var formatted = Iec61850ValueFormatter.Format(value, "Float", "V");

        Assert.Equal(value, formatted);
    }
}
