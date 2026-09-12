using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatP2EvidenceHydrationTests
{
    [Fact]
    public async Task PersistThenHydrate_RestoresOnlySparseEvidenceForCanonicalRows()
    {
        var root = TempRoot();
        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);
            var device = Device();
            var first = Point(device.DeviceId, "Breaker", "AA1E1F06R4LD0/XCBR1.Pos.stVal");
            var second = Point(device.DeviceId, "Trip", "AA1E1F06R4LD0/GGIO1.Ind1.stVal");
            device.Points.Add(first);
            device.Points.Add(second);

            var saved = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.Write(saved, first, NativeFatEvidenceField.Value1, "Open [01]");
            NativeFatCanonicalEvidenceOverlay.Write(saved, first, NativeFatEvidenceField.Value2, "Closed [10]");
            NativeFatCanonicalEvidenceOverlay.Write(saved, first, NativeFatEvidenceField.Result, "PASS");
            await service.SaveAsync(device, saved);

            var result = await service.HydrateAsync(device);
            var restored = new NativeFatIedSessionCacheState();
            var merged = NativeFatCanonicalEvidenceOverlay.MergeMissing(restored, result.EvidenceByRow);

            Assert.True(result.Succeeded);
            Assert.True(result.SnapshotFound);
            Assert.Equal(1, result.LoadedRows);
            Assert.Equal(1, merged);
            Assert.Equal("Open [01]", NativeFatCanonicalEvidenceOverlay.Read(restored, first, NativeFatEvidenceField.Value1));
            Assert.Equal("Closed [10]", NativeFatCanonicalEvidenceOverlay.Read(restored, first, NativeFatEvidenceField.Value2));
            Assert.Equal("PASS", NativeFatCanonicalEvidenceOverlay.Read(restored, first, NativeFatEvidenceField.Result));
            Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.Read(restored, second, NativeFatEvidenceField.Value1));
            Assert.Equal(2, device.Points.Count);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task HydrationMerge_NeverOverwritesEvidenceCapturedAfterHydrationStarted()
    {
        var root = TempRoot();
        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);
            var device = Device();
            var point = Point(device.DeviceId, "Breaker", "AA1E1F06R4LD0/XCBR1.Pos.stVal");
            device.Points.Add(point);

            var persisted = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.Write(persisted, point, NativeFatEvidenceField.Value1, "OLD-V1");
            NativeFatCanonicalEvidenceOverlay.Write(persisted, point, NativeFatEvidenceField.Value2, "OLD-V2");
            NativeFatCanonicalEvidenceOverlay.Write(persisted, point, NativeFatEvidenceField.Result, "PASS");
            await service.SaveAsync(device, persisted);

            var result = await service.HydrateAsync(device);
            var live = new NativeFatIedSessionCacheState();
            NativeFatCanonicalEvidenceOverlay.Write(live, point, NativeFatEvidenceField.Value1, "NEW-LIVE-V1");

            NativeFatCanonicalEvidenceOverlay.MergeMissing(live, result.EvidenceByRow);

            Assert.Equal("NEW-LIVE-V1", NativeFatCanonicalEvidenceOverlay.Read(live, point, NativeFatEvidenceField.Value1));
            Assert.Equal("OLD-V2", NativeFatCanonicalEvidenceOverlay.Read(live, point, NativeFatEvidenceField.Value2));
            Assert.Equal("PASS", NativeFatCanonicalEvidenceOverlay.Read(live, point, NativeFatEvidenceField.Result));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task MissingSnapshot_ResolvesAsEmptyWithoutInventingEvidence()
    {
        var root = TempRoot();
        try
        {
            using var service = new NativeFatEvidenceHydrationService(root);
            var device = Device();
            device.Points.Add(Point(device.DeviceId, "Trip", "AA1E1F06R4LD0/GGIO1.Ind1.stVal"));

            var result = await service.HydrateAsync(device);

            Assert.True(result.Succeeded);
            Assert.False(result.SnapshotFound);
            Assert.Empty(result.EvidenceByRow);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(0, "·")]
    [InlineData(1, "··")]
    [InlineData(2, "···")]
    [InlineData(3, "·")]
    public void RollingDots_UsesOneSharedPhase(int phase, string expected)
        => Assert.Equal(expected, NativeFatEvidenceLoadingPresentation.RollingDots(phase));

    [Fact]
    public void P2_GridBindsCanonicalRowsBeforeStartingEvidenceHydration_AndUsesOneClock()
    {
        var gridSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var serviceSource = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatEvidenceHydrationService.cs"));

        var bind = gridSource.IndexOf("_nativeFatCanonicalGrid.ItemsSource = device?.Points;", StringComparison.Ordinal);
        var hydrate = gridSource.IndexOf("BeginNativeFatEvidenceHydration(device);", bind, StringComparison.Ordinal);
        Assert.True(bind >= 0);
        Assert.True(hydrate > bind, "Canonical Engineering rows must be visible before evidence hydration begins.");

        Assert.Contains("DispatcherTimer? _nativeFatEvidenceClock", gridSource, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(gridSource, "new DispatcherTimer"));
        Assert.Contains("NativeFatEvidenceLoadingPresentation.RollingDots", gridSource, StringComparison.Ordinal);
        Assert.Contains("cache.IsEvidenceHydrating", gridSource, StringComparison.Ordinal);

        Assert.DoesNotContain("PrepareIoTestIedForFatAsync", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenDescribedSourcesAsync", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IoFatEngineeringWorkspaceProjectionService", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAndDiscoverAsync", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitoringAsync", serviceSource, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string token)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }
        return count;
    }

    private static Iec61850MonitorDevice Device()
        => new()
        {
            DeviceId = "dev-aa1e1f06r4",
            Name = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = true,
            IsMonitoring = true
        };

    private static Iec61850MonitorPoint Point(string deviceId, string name, string reference)
        => new()
        {
            DeviceId = deviceId,
            DeviceName = "AA1E1F06R4",
            SignalName = name,
            IecReference = reference,
            IecDataType = "BOOLEAN",
            Quality = "Good",
            Status = "Live",
            Value = "False"
        };

    private static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "arsas-native-fat-p2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

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
