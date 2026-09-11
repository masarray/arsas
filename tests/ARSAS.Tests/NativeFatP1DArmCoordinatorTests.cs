using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatP1DArmCoordinatorTests
{
    [Fact]
    public void Arm_RequiresAlreadyRunningEngineeringMonitoring()
    {
        using var coordinator = new NativeFatArmCoordinator();
        var device = Device(isConnected: true, isMonitoring: false);
        var cache = new NativeFatIedSessionCacheState();

        var result = coordinator.Arm(device, cache);

        Assert.False(result.Succeeded);
        Assert.False(cache.IsArmed);
        Assert.Empty(cache.EvidenceByRow);
    }

    [Fact]
    public void Arm_SeedsValue1AndCapturesValue2WithoutReplacingCanonicalRows()
    {
        using var coordinator = new NativeFatArmCoordinator();
        var device = Device(isConnected: true, isMonitoring: true);
        var point = Point(device.DeviceId, "Breaker", "AA1E1F06R4LD0/XCBR1.Pos.stVal", "Open [01]");
        device.Points.Add(point);
        var canonicalReference = device.Points[0];
        var cache = new NativeFatIedSessionCacheState();

        var result = coordinator.Arm(device, cache);

        Assert.True(result.Succeeded);
        Assert.True(cache.IsArmed);
        Assert.Equal(1, result.ArmedRows);
        Assert.Equal(1, result.SeededValue1Rows);
        Assert.Single(device.Points);
        Assert.Same(canonicalReference, device.Points[0]);
        Assert.Equal("Open [01]", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value2));

        point.Value = "Closed [10]";

        Assert.Single(device.Points);
        Assert.Same(canonicalReference, device.Points[0]);
        Assert.Equal("Open [01]", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal("Closed [10]", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value2));
        Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Result));
    }

    [Fact]
    public void Arm_LatestMeaningfulTransitionRollsValuePairAndLeavesResultOperatorOwned()
    {
        using var coordinator = new NativeFatArmCoordinator();
        var device = Device(isConnected: true, isMonitoring: true);
        var point = Point(device.DeviceId, "Status", "AA1E1F06R4LD0/GGIO1.Ind1.stVal", "False");
        device.Points.Add(point);
        var cache = new NativeFatIedSessionCacheState();
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Result, "REVIEW");

        Assert.True(coordinator.Arm(device, cache).Succeeded);
        point.Value = "True";
        point.Value = "False";

        Assert.Equal("True", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal("False", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value2));
        Assert.Equal("REVIEW", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Result));
    }

    [Fact]
    public void NativeStartHandler_IsSynchronousArmOnlyAndCannotEnterLegacyPreparation()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var method = ExtractMethod(source, "private void NativeFatStartButton_Click(object sender, RoutedEventArgs e)");

        Assert.Contains("Stopwatch.StartNew()", method, StringComparison.Ordinal);
        Assert.Contains("_nativeFatArmCoordinator.Arm(device, cache)", method, StringComparison.Ordinal);
        Assert.Contains("networkPrepare=false", method, StringComparison.Ordinal);
        Assert.Contains("reconnect=false", method, StringComparison.Ordinal);
        Assert.Contains("sclImport=false", method, StringComparison.Ordinal);
        Assert.Contains("discovery=false", method, StringComparison.Ordinal);
        Assert.Contains("reportRestart=false", method, StringComparison.Ordinal);
        Assert.Contains("pollingChange=false", method, StringComparison.Ordinal);

        Assert.DoesNotContain("await ", method, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareIoTestIedForFatAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenDescribedSourcesAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAndDiscoverAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitoringAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("IoFatEngineeringWorkspaceProjectionService", method, StringComparison.Ordinal);
    }

    private static Iec61850MonitorDevice Device(bool isConnected, bool isMonitoring)
        => new()
        {
            DeviceId = "dev-aa1e1f06r4",
            Name = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = isConnected,
            IsMonitoring = isMonitoring
        };

    private static Iec61850MonitorPoint Point(
        string deviceId,
        string signalName,
        string reference,
        string value)
        => new()
        {
            DeviceId = deviceId,
            DeviceName = "AA1E1F06R4",
            SignalName = signalName,
            IecReference = reference,
            IecDataType = "BOOLEAN",
            Quality = "Good",
            Status = "Live",
            SourceMode = "Static DataSet reporting",
            Value = value
        };

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method '{signature}'.");
        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace >= 0, $"Could not find opening brace for '{signature}'.");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[start..(index + 1)];
        }

        throw new InvalidDataException($"Method '{signature}' has no balanced closing brace.");
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
