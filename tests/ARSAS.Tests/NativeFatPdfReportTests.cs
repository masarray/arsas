using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class NativeFatPdfReportTests
{
    [Fact]
    public void Generate_ProducesPdfFromImmutableNativeSnapshot()
    {
        var snapshot = new NativeFatReportSnapshot(
            DeviceId: "pdf-device-01",
            IedName: "Relay PDF",
            IpAddress: "192.0.2.55",
            Port: 102,
            GeneratedUtc: new DateTimeOffset(2026, 9, 9, 9, 15, 0, TimeSpan.Zero),
            Rows:
            [
                new NativeFatReportRow(
                    SignalName: "Breaker status",
                    IecReference: "IED1LD0/GGIO1.Ind1.stVal",
                    FunctionalConstraint: "ST",
                    DataType: "Boolean",
                    IsHistorical: false,
                    Result: NativeFatResult.Pass,
                    HistoryCount: 2,
                    Value1Text: "False",
                    Value1Quality: "Good",
                    Value1DeviceTimestamp: "2026-09-09 09:00:00.001",
                    Value1SourceMode: "BRCB",
                    Value1CapturedUtc: new DateTimeOffset(2026, 9, 9, 9, 0, 1, TimeSpan.Zero),
                    Value2Text: "True",
                    Value2Quality: "Good",
                    Value2DeviceTimestamp: "2026-09-09 09:00:05.002",
                    Value2SourceMode: "BRCB",
                    Value2CapturedUtc: new DateTimeOffset(2026, 9, 9, 9, 0, 6, TimeSpan.Zero)),
                new NativeFatReportRow(
                    SignalName: "Removed old DI",
                    IecReference: "IED1LD0/GGIO1.Ind9.stVal",
                    FunctionalConstraint: "ST",
                    DataType: "Boolean",
                    IsHistorical: true,
                    Result: NativeFatResult.Fail,
                    HistoryCount: 4,
                    Value1Text: "False",
                    Value1Quality: "Good",
                    Value1DeviceTimestamp: "2026-09-08 08:00:00.001",
                    Value1SourceMode: "MMS polling",
                    Value1CapturedUtc: new DateTimeOffset(2026, 9, 8, 8, 0, 1, TimeSpan.Zero),
                    Value2Text: "True",
                    Value2Quality: "Good",
                    Value2DeviceTimestamp: "2026-09-08 08:00:04.001",
                    Value2SourceMode: "MMS polling",
                    Value2CapturedUtc: new DateTimeOffset(2026, 9, 8, 8, 0, 5, TimeSpan.Zero))
            ]);

        var currentOnly = NativeFatPdfReportService.Generate(snapshot, includeHistorical: false);
        var withHistory = NativeFatPdfReportService.Generate(snapshot, includeHistorical: true);

        Assert.True(currentOnly.Length > 4_000);
        Assert.True(withHistory.Length > currentOnly.Length);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(currentOnly, 0, 8));

        var ascii = Encoding.ASCII.GetString(withHistory);
        Assert.Contains("ARSAS Native FAT - Relay PDF - IEC 61850 FAT Evidence Report", ascii, StringComparison.Ordinal);
        Assert.Contains("Breaker status", ascii, StringComparison.Ordinal);
        Assert.Contains("Removed old DI", ascii, StringComparison.Ordinal);
        Assert.Contains("historical included: 1", ascii, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%%EOF", ascii, StringComparison.Ordinal);
    }
}
