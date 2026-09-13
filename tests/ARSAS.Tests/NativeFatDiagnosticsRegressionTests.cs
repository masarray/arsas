using System.Globalization;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatDiagnosticsRegressionTests
{
    [Fact]
    public void ComtradeCount_UsesOnlyDistinctFilesReturnedByFileDirectoryCatalog()
    {
        var modified = new DateTimeOffset(2026, 9, 13, 3, 0, 0, TimeSpan.Zero);
        var records = new[]
        {
            BuildRecord("FRA00027", modified, "FRA00027.cfg", "FRA00027.dat"),
            BuildRecord("FRA00028", modified.AddMinutes(1), "FRA00028.cfg", "FRA00028.dat", "FRA00028.hdr"),
            BuildRecord("FRA00028-copy", modified.AddMinutes(2), "FRA00028.cfg")
        };

        Assert.Equal(5, NativeFatComtradeDiagnosticService.CountDetectedFiles(records));
        Assert.Equal(0, NativeFatComtradeDiagnosticService.CountDetectedFiles(Array.Empty<Iec61850FaultRecordSet>()));
    }

    [Fact]
    public void TimeSync_LtmsPlusFreshIndependentTimestamp_IsOk()
    {
        var now = new DateTimeOffset(2026, 9, 13, 3, 15, 0, TimeSpan.Zero);
        var device = BuildDevice(
            Point("LTMS health", "IEDLD0/LTMS1.Health.stVal", "Good", "Good", now.AddSeconds(-1)),
            Point("Breaker event", "IEDLD0/XCBR1.Pos.stVal", "Open [01]", "Good", now.AddSeconds(-2)));

        var result = NativeFatTimeSyncDiagnosticService.Evaluate(device, now);

        Assert.True(result.IsSynchronized);
        Assert.Equal("OK", result.Verdict);
        Assert.True(result.LtmsPresent);
        Assert.True(result.LtmsTrusted);
        Assert.Equal(1, result.FreshPrimaryTimestampCount);
    }

    [Fact]
    public void TimeSync_PositiveTimeSynchrnzWithoutPrimaryCrossCheck_NeverGrantsOk()
    {
        var now = new DateTimeOffset(2026, 9, 13, 3, 15, 0, TimeSpan.Zero);
        var device = BuildDevice(
            Point("LTMS health", "IEDLD0/LTMS1.Health.stVal", "Good", "Good", now.AddSeconds(-1)),
            Point("TimeSynchrnz", "IEDLD0/LLN0.TimeSynchrnz.stVal", "true", "Good", now.AddSeconds(-1)));

        var result = NativeFatTimeSyncDiagnosticService.Evaluate(device, now);

        Assert.False(result.IsSynchronized);
        Assert.Equal("REVIEW", result.Verdict);
        Assert.Equal(0, result.FreshPrimaryTimestampCount);
        Assert.Contains(result.SecondaryTelemetry, item => item.IecReference.Contains("TimeSynchrnz", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TimeSync_WithoutLtms_RequiresTwoIndependentFreshGoodTimestamps()
    {
        var now = new DateTimeOffset(2026, 9, 13, 3, 15, 0, TimeSpan.Zero);
        var onePoint = BuildDevice(
            Point("Breaker event", "IEDLD0/XCBR1.Pos.stVal", "Open [01]", "Good", now.AddSeconds(-1)));
        var twoPoints = BuildDevice(
            Point("Breaker event", "IEDLD0/XCBR1.Pos.stVal", "Open [01]", "Good", now.AddSeconds(-1)),
            Point("Disconnector event", "IEDLD0/XSWI1.Pos.stVal", "Closed [10]", "Good", now.AddSeconds(-3)));

        var insufficient = NativeFatTimeSyncDiagnosticService.Evaluate(onePoint, now);
        var fallback = NativeFatTimeSyncDiagnosticService.Evaluate(twoPoints, now);

        Assert.False(insufficient.IsSynchronized);
        Assert.Equal("REVIEW", insufficient.Verdict);
        Assert.True(fallback.IsSynchronized);
        Assert.Equal("OK", fallback.Verdict);
        Assert.False(fallback.LtmsPresent);
        Assert.Equal(2, fallback.FreshPrimaryTimestampCount);
    }

    [Fact]
    public void TimeSync_ExplicitNegativeVendorStatus_VetoesOtherwiseGoodPrimaryEvidence()
    {
        var now = new DateTimeOffset(2026, 9, 13, 3, 15, 0, TimeSpan.Zero);
        var device = BuildDevice(
            Point("LTMS health", "IEDLD0/LTMS1.Health.stVal", "Good", "Good", now.AddSeconds(-1)),
            Point("Breaker event", "IEDLD0/XCBR1.Pos.stVal", "Open [01]", "Good", now.AddSeconds(-1)),
            Point("TimeSynchrnz", "IEDLD0/LLN0.TimeSynchrnz.stVal", "false", "Good", now.AddSeconds(-1)));

        var result = NativeFatTimeSyncDiagnosticService.Evaluate(device, now);

        Assert.False(result.IsSynchronized);
        Assert.Equal("NOT OK", result.Verdict);
        Assert.True(result.ExplicitNegativeSyncStatus);
    }

    [Fact]
    public void TimeSync_StaleOrBadQualityTimestamp_FailsClosed()
    {
        var now = new DateTimeOffset(2026, 9, 13, 3, 15, 0, TimeSpan.Zero);
        var device = BuildDevice(
            Point("LTMS health", "IEDLD0/LTMS1.Health.stVal", "Good", "Good", now.AddSeconds(-1)),
            Point("Stale event", "IEDLD0/XCBR1.Pos.stVal", "Open [01]", "Good", now.AddMinutes(-2)),
            Point("Bad quality event", "IEDLD0/XSWI1.Pos.stVal", "Closed [10]", "Invalid", now.AddSeconds(-1)));

        var result = NativeFatTimeSyncDiagnosticService.Evaluate(device, now);

        Assert.False(result.IsSynchronized);
        Assert.Equal("REVIEW", result.Verdict);
        Assert.Equal(0, result.FreshPrimaryTimestampCount);
    }

    [Fact]
    public void NativeFatDiagnostics_ReusesExistingFileWorkflow_AndDoesNotStartSecondAcquisition()
    {
        var ui = File.ReadAllText(FindRepoFile("MainWindow.NativeFatDiagnostics.cs"));
        var pivot = File.ReadAllText(FindRepoFile("MainWindow.ProductionFatTab.cs"));
        var service = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatDiagnosticsService.cs"));

        Assert.Contains("COMTRADE {fileCount} Files", ui, StringComparison.Ordinal);
        Assert.Contains("new FaultRecordWindow(device.Name, device.IpAddress, device.Port)", ui, StringComparison.Ordinal);
        Assert.Contains("Time Sync OK", ui, StringComparison.Ordinal);
        Assert.Contains("SNTP server activity", ui, StringComparison.Ordinal);
        Assert.Contains("NativeFatTimeSyncDiagnosticService.Evaluate", ui, StringComparison.Ordinal);
        Assert.Contains("InstallNativeFatDiagnosticButtons()", pivot, StringComparison.Ordinal);
        Assert.Contains("BindNativeFatDiagnostics(SelectedDevice)", pivot, StringComparison.Ordinal);
        Assert.Contains("IEC 61850 FileDirectory", service, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "ConnectAndDiscoverAsync",
                     "StartMonitoringAsync",
                     "PrepareIoTestIedForFatAsync",
                     "OpenDescribedSourcesAsync",
                     "IoFatEngineeringWorkspaceProjectionService"
                 })
        {
            Assert.DoesNotContain(forbidden, ui, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, service, StringComparison.Ordinal);
        }
    }

    private static Iec61850MonitorDevice BuildDevice(params Iec61850MonitorPoint[] points)
    {
        var device = new Iec61850MonitorDevice
        {
            DeviceId = "native-fat-time-sync-test",
            Name = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = true,
            IsMonitoring = true
        };
        foreach (var point in points)
        {
            point.DeviceId = device.DeviceId;
            point.DeviceName = device.Name;
            point.IpAddress = device.IpAddress;
            device.Points.Add(point);
        }
        return device;
    }

    private static Iec61850MonitorPoint Point(
        string name,
        string reference,
        string value,
        string quality,
        DateTimeOffset timestamp)
        => new()
        {
            SignalName = name,
            IecReference = reference,
            Value = value,
            Quality = quality,
            DeviceTimestamp = timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            SourceMode = "IEC 61850 report",
            Status = "Live"
        };

    private static Iec61850FaultRecordSet BuildRecord(
        string baseName,
        DateTimeOffset modified,
        params string[] fileNames)
        => new()
        {
            RecordId = baseName,
            BaseName = baseName,
            RemoteDirectory = string.Empty,
            LastModifiedUtc = modified,
            Completeness = "Detected",
            Files = fileNames.Select(name => new Iec61850FaultRecordFile
            {
                Name = name,
                RemotePath = name,
                BaseName = Path.GetFileNameWithoutExtension(name),
                Extension = Path.GetExtension(name),
                LastModifiedUtc = modified,
                SizeBytes = 1024
            }).ToArray()
        };

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

        throw new DirectoryNotFoundException(
            $"Could not locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
