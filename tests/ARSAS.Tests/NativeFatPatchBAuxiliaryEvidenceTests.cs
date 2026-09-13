using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatPatchBAuxiliaryEvidenceTests
{
    [Fact]
    public void ComtradeVerifiedRecords_AppearWithRequiredColumnsAndImmutableValues()
    {
        var device = Device("runtime-a", "AA1E1F06R4");
        var files = new List<Iec61850FaultRecordFile>
        {
            new()
            {
                Name = "FAULT_001.cfg",
                RemotePath = "/COMTRADE/FAULT_001.cfg",
                BaseName = "FAULT_001",
                SizeBytes = 512,
                LastModifiedUtc = new DateTimeOffset(2026, 9, 10, 8, 30, 0, TimeSpan.Zero)
            },
            new()
            {
                Name = "FAULT_001.dat",
                RemotePath = "/COMTRADE/FAULT_001.dat",
                BaseName = "FAULT_001",
                SizeBytes = 1024,
                LastModifiedUtc = new DateTimeOffset(2026, 9, 10, 8, 31, 0, TimeSpan.Zero)
            }
        };
        var records = new List<Iec61850FaultRecordSet>
        {
            new()
            {
                RecordId = "/COMTRADE/FAULT_001",
                BaseName = "FAULT_001",
                KnownSizeBytes = 1536,
                LastModifiedUtc = new DateTimeOffset(2026, 9, 10, 8, 31, 0, TimeSpan.Zero),
                Files = files
            }
        };
        var cache = new NativeFatAuxiliaryEvidenceCache();
        cache.RecordComtradeDiscovery(
            device,
            records,
            new DateTimeOffset(2026, 9, 10, 8, 32, 0, TimeSpan.Zero));

        var auxiliary = cache.Capture(device);
        records.Clear();
        files.Clear();
        var snapshot = NativeFatPrintPreviewSnapshot.Capture(
            device,
            new NativeFatIedSessionCacheState(),
            auxiliary);
        var layout = NativeFatP4DReportAdapter.Build(snapshot);
        var text = ReportText(layout);
        var pdf = IoFatPdfReportService.GenerateLayout(layout, snapshot.IedName, "COMTRADE");

        Assert.Single(snapshot.AuxiliaryEvidence.ComtradeRecords);
        Assert.Equal("FAULT_001", snapshot.AuxiliaryEvidence.ComtradeRecords[0].RecordName);
        Assert.Contains("IEC 61850 Fault Record (COMTRADE)", text);
        Assert.Contains("Available Fault Records", text);
        Assert.Contains("Record Name", text);
        Assert.Contains("Record Date", text);
        Assert.Contains("Size", text);
        Assert.Contains("Result", text);
        Assert.Contains("FAULT_001", text);
        Assert.Contains("2026-09-10 08:31:00 UTC", text);
        Assert.Contains("1.5 KB", text);
        Assert.Contains("OK", text);
        Assert.Equal("%PDF-1.4", System.Text.Encoding.ASCII.GetString(pdf, 0, 8));
        for (var index = 0; index < layout.Pages.Count; index++)
        {
            var expected = $"Page {index + 1} / {layout.Pages.Count}";
            Assert.Contains(
                layout.Pages[index].Commands.OfType<IoFatReportTextCommand>(),
                command => command.Text == expected);
        }
    }

    [Fact]
    public void ComtradeFailedOrEmptyDiscovery_OmitsWholeSection()
    {
        var device = Device("runtime-a", "AA1E1F06R4");
        var cache = new NativeFatAuxiliaryEvidenceCache();

        Assert.DoesNotContain(
            "IEC 61850 Fault Record (COMTRADE)",
            ReportText(Build(device, cache)));

        cache.RecordComtradeDiscovery(
            device,
            [new Iec61850FaultRecordSet { RecordId = "EMPTY", BaseName = "EMPTY" }],
            DateTimeOffset.UtcNow);
        Assert.DoesNotContain(
            "IEC 61850 Fault Record (COMTRADE)",
            ReportText(Build(device, cache)));

        cache.RecordComtradeDiscovery(device, [ValidRecord("FAULT_002")], DateTimeOffset.UtcNow);
        cache.ClearComtrade(device);
        Assert.DoesNotContain(
            "IEC 61850 Fault Record (COMTRADE)",
            ReportText(Build(device, cache)));
    }

    [Fact]
    public void TimeSyncOk_AppearsWithOnlyBoundedSupportingEvidence()
    {
        var device = Device("runtime-a", "AA1E1F06R4");
        var cache = new NativeFatAuxiliaryEvidenceCache();
        var diagnostic = Diagnostic(
            true,
            "OK",
            "LTMS evidence is present and cross-checked by a fresh good-quality IEC timestamp.",
            false);

        cache.RecordTimeSyncEvaluation(
            device,
            diagnostic,
            new DateTimeOffset(2026, 9, 10, 8, 40, 0, TimeSpan.Zero));
        var snapshot = NativeFatPrintPreviewSnapshot.Capture(
            device,
            new NativeFatIedSessionCacheState(),
            cache.Capture(device));
        var text = ReportText(NativeFatP4DReportAdapter.Build(snapshot));

        Assert.NotNull(snapshot.AuxiliaryEvidence.TimeSync);
        Assert.True(snapshot.AuxiliaryEvidence.TimeSync!.IsSynchronized);
        Assert.Equal(2, snapshot.AuxiliaryEvidence.TimeSync.SupportingPoints.Count);
        Assert.Contains("IEC 61850 Time Synchronization Evidence", text);
        Assert.Contains("Time Sync OK", text);
        Assert.Contains(text, value => value.Contains("LTMS verified", StringComparison.Ordinal));
        Assert.Contains("AA1E1F06R4LD0/LLN0.LTMS", text);
        Assert.Contains("AA1E1F06R4LD0/XCBR1.Pos.stVal", text);
    }

    [Theory]
    [InlineData("REVIEW", false)]
    [InlineData("NOT OK", true)]
    public void TimeSyncReviewOrNotOk_OmitsWholeSection(string verdict, bool explicitNegative)
    {
        var device = Device("runtime-a", "AA1E1F06R4");
        var cache = new NativeFatAuxiliaryEvidenceCache();
        cache.RecordTimeSyncEvaluation(device, Diagnostic(true, "OK", "Verified.", false), DateTimeOffset.UtcNow);
        cache.RecordTimeSyncEvaluation(
            device,
            Diagnostic(false, verdict, "Synchronization is not proven.", explicitNegative),
            DateTimeOffset.UtcNow);

        var text = ReportText(Build(device, cache));
        Assert.DoesNotContain("IEC 61850 Time Synchronization Evidence", text);
        Assert.DoesNotContain("Time Sync OK", text);
    }

    [Fact]
    public void Cache_IsScopedByStableIedNameAcrossRuntimeDeviceIds()
    {
        var first = Device("runtime-a", "DISPLAY-A");
        first.SclIedName = "AA1E1F06R4";
        var reopened = Device("runtime-b", "DISPLAY-B");
        reopened.SclIedName = "AA1E1F06R4";
        var other = Device("runtime-c", "AA1E1F06R5");
        var cache = new NativeFatAuxiliaryEvidenceCache();

        cache.RecordComtradeDiscovery(first, [ValidRecord("FAULT_STABLE")], DateTimeOffset.UtcNow);

        Assert.Single(cache.Capture(reopened).ComtradeRecords);
        Assert.Empty(cache.Capture(other).ComtradeRecords);
    }

    [Fact]
    public void PreviewAndSnapshotSources_DoNotStartDiscoveryReconnectAcquisitionOrLegacyFatWindow()
    {
        var preview = File.ReadAllText(FindRepoFile("MainWindow.NativeFatPrintPreview.cs"));
        var snapshot = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatPrintPreviewSnapshot.cs"));
        var decorator = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatAuxiliaryReportDecorator.cs"));

        Assert.Contains("_nativeFatAuxiliaryEvidenceCache.Capture(device)", preview, StringComparison.Ordinal);
        foreach (var source in new[] { preview, snapshot, decorator })
        {
            Assert.DoesNotContain("FaultRecordTransferClient", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DiscoverAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ConnectAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("StartMonitoringAsync", source, StringComparison.Ordinal);
            Assert.DoesNotContain("IoListTestingWindow", source, StringComparison.Ordinal);
        }
    }

    private static IoFatReportLayoutPlan Build(
        Iec61850MonitorDevice device,
        NativeFatAuxiliaryEvidenceCache cache)
        => NativeFatP4DReportAdapter.Build(NativeFatPrintPreviewSnapshot.Capture(
            device,
            new NativeFatIedSessionCacheState(),
            cache.Capture(device)));

    private static string[] ReportText(IoFatReportLayoutPlan layout)
        => layout.Pages
            .SelectMany(page => page.Commands)
            .OfType<IoFatReportTextCommand>()
            .Select(command => command.Text)
            .ToArray();

    private static Iec61850MonitorDevice Device(string deviceId, string name)
        => new()
        {
            DeviceId = deviceId,
            Name = name,
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = true
        };

    private static Iec61850FaultRecordSet ValidRecord(string name)
        => new()
        {
            RecordId = $"/COMTRADE/{name}",
            BaseName = name,
            KnownSizeBytes = 2048,
            LastModifiedUtc = DateTimeOffset.UtcNow,
            Files =
            [
                new Iec61850FaultRecordFile
                {
                    Name = $"{name}.cff",
                    RemotePath = $"/COMTRADE/{name}.cff",
                    BaseName = name,
                    SizeBytes = 2048,
                    LastModifiedUtc = DateTimeOffset.UtcNow
                }
            ]
        };

    private static NativeFatTimeSyncDiagnosticResult Diagnostic(
        bool synchronized,
        string verdict,
        string summary,
        bool explicitNegative)
        => new(
            synchronized,
            verdict,
            summary,
            true,
            synchronized,
            synchronized ? 1 : 0,
            explicitNegative,
            synchronized
                ?
                [
                    new NativeFatTimeSyncPointEvidence(
                        "LTMS",
                        "LTMS",
                        "AA1E1F06R4LD0/LLN0.LTMS",
                        "2026-09-10T08:40:00Z",
                        "Good",
                        "2026-09-10T08:40:00Z",
                        0.02,
                        true),
                    new NativeFatTimeSyncPointEvidence(
                        "IEC timestamp",
                        "Breaker",
                        "AA1E1F06R4LD0/XCBR1.Pos.stVal",
                        "Open [01]",
                        "Good",
                        "2026-09-10T08:40:00Z",
                        0.03,
                        true),
                    new NativeFatTimeSyncPointEvidence(
                        "IEC timestamp",
                        "Extra",
                        "AA1E1F06R4LD0/GGIO1.Ind1.stVal",
                        "False",
                        "Good",
                        "2026-09-10T08:40:00Z",
                        0.04,
                        true)
                ]
                : Array.Empty<NativeFatTimeSyncPointEvidence>(),
            Array.Empty<NativeFatTimeSyncPointEvidence>());

    private static string FindRepoFile(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
