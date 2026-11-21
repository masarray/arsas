using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class EngineeringClipboardTextTests
{
    private static IReadOnlyList<string?> Row(params string?[] cells) => cells;

    [Fact]
    public void ExcelBlockPreservesColumnOrderSelectionGapsAndCrLf()
    {
        var ok = EngineeringClipboardText.TryBuild(
            ["IED", "Signal", "Value"],
            [
                Row("SG7_BCU", "CBXCBR1.Pos", "off"),
                Row("SG7_BCU", "", "on")
            ], true, out var copied, out var reason);

        Assert.True(ok, reason);
        Assert.Equal("IED\tSignal\tValue\r\nSG7_BCU\tCBXCBR1.Pos\toff\r\nSG7_BCU\t\ton",
            copied);
    }

    [Fact]
    public void TsvEscapesMultilineTabsAndQuotesWithoutLosingEngineeringContent()
    {
        Assert.True(EngineeringClipboardText.TryBuild(
            ["Report", "Value"],
            [Row("A\tB", "value \"quoted\"\nsecond")],
            true, out var copied, out _));
        Assert.Equal("Report\tValue\r\n\"A\tB\"\t\"value \"\"quoted\"\"\nsecond\"", copied);
    }

    [Fact]
    public void OwnerBytesAreCopiedExactlyWithoutInferencingClientIdentity()
    {
        Assert.True(EngineeringClipboardText.TryBuild(
            ["Owner"],
            [Row("192.16.1.73 (x'C0100149')")], false,
            out var copied, out _));
        Assert.Equal("192.16.1.73 (x'C0100149')", copied);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"bad\")", "'=HYPERLINK(\"bad\")")]
    [InlineData("@cell", "'@cell")]
    [InlineData("+RUN", "'+RUN")]
    [InlineData("-RUN", "'-RUN")]
    [InlineData("-1730", "-1730")]
    [InlineData("+4.2", "+4.2")]
    [InlineData("0", "0")]
    public void SpreadsheetPasteDoesNotExecuteTextAsFormulas(
        string input, string expected)
    {
        Assert.True(EngineeringClipboardText.TryBuild(
            ["Value"], [Row(input)], false, out var copied, out _));
        Assert.Equal(expected, copied);
    }

    [Fact]
    public void OversizedSelectionFailsClosedWithoutPartialClipboardData()
    {
        var many = Enumerable.Range(0, EngineeringClipboardText.MaxRows + 1)
            .Select(number => Row(number.ToString()));
        Assert.False(EngineeringClipboardText.TryBuild(
            ["Row"], many, true, out var copied, out var reason));
        Assert.Empty(copied);
        Assert.Contains("Export CSV", reason);

        Assert.False(EngineeringClipboardText.TryBuild(
            ["Value"], [Row(new string('V', EngineeringClipboardText.MaxCharacters + 1))],
            true, out copied, out reason));
        Assert.Empty(copied);
        Assert.Contains("512 KB", reason);
    }

    [Fact]
    public void NullAndEmptyRowsNeverClaimSuccessfulCopy()
    {
        Assert.False(EngineeringClipboardText.TryBuild(
            [], [], true, out var none, out _));
        Assert.Empty(none);
        Assert.False(EngineeringClipboardText.TryBuild(
            ["Name"], [], true, out none, out _));
        Assert.Empty(none);
        Assert.True(EngineeringClipboardText.TryBuild(
            ["Name"], [Row(null)], true, out var text, out _));
        Assert.Equal("Name\r\n", text);
    }

    [Fact]
    public void EmptyFirstSelectedRowStillPreservesExcelBlockRowSeparation()
    {
        Assert.True(EngineeringClipboardText.TryBuild(
            ["Value"], [Row(""), Row("connected")],
            false, out var text, out var reason), reason);
        Assert.Equal("\r\nconnected", text);
    }

    [Fact]
    public void AChangedColumnCountRejectsInconsistentSnapshot()
    {
        Assert.False(EngineeringClipboardText.TryBuild(
            ["One", "Two"], [Row("one")], true, out var text, out var reason));
        Assert.Empty(text);
        Assert.Contains("columns changed", reason);
    }
}
