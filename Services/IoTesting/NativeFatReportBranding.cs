namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Shared vector branding for native FAT report layouts. The mark is expressed only through
/// report commands so the WPF preview and PDF writer render the exact same logo without an
/// external bitmap dependency.
/// </summary>
internal static class NativeFatReportBranding
{
    private static readonly IoFatReportColor Navy = IoFatReportColor.FromHex("0F172A");
    private static readonly IoFatReportColor Blue = IoFatReportColor.FromHex("2563EB");
    private static readonly IoFatReportColor White = IoFatReportColor.FromHex("FFFFFF");

    public static void AddLogo(ICollection<IoFatReportCommand> commands, double x, double topY)
    {
        ArgumentNullException.ThrowIfNull(commands);

        commands.Add(new IoFatReportRectCommand(
            x,
            topY,
            22d,
            22d,
            4d,
            Blue,
            Blue,
            0d));
        commands.Add(new IoFatReportTextCommand(
            x + 5.2d,
            topY - 15.2d,
            12d,
            "A",
            IoFatReportFontKind.Bold,
            11.2d,
            White));
        commands.Add(new IoFatReportTextCommand(
            x + 29d,
            topY - 15.4d,
            72d,
            "ARSAS",
            IoFatReportFontKind.Bold,
            10.2d,
            Navy));
    }
}