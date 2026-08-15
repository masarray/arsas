using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class Iec61850ValueFormatterTests
{
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
    public void Format_Does_Not_Collapse_NonStVal_Structures()
    {
        const string value = "Structure(3) {mag=123.4, q=Quality{V=1,D=0}, t=BinaryTime(2026-08-15 16:11:57.723, Q=0x0A, ext=True)}";

        var formatted = Iec61850ValueFormatter.Format(value, "Float", "V");

        Assert.Equal(value, formatted);
    }
}
