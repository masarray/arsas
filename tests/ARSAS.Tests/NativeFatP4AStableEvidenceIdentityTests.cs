using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatP4AStableEvidenceIdentityTests
{
    [Fact]
    public void RecreatedAndReorderedRows_KeepCswiEvidenceOnExactTelegram_NotThdPpv()
    {
        var cache = new NativeFatIedSessionCacheState();
        var cswiBefore = Point(
            "runtime-before",
            "CSWI.Pos",
            "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            "Open [01]");
        NativeFatCanonicalEvidenceOverlay.Write(
            cache,
            cswiBefore,
            NativeFatEvidenceField.Value1,
            "Open [01]");

        // Runtime DeviceId and display labels change, and row order is deliberately reversed.
        // Stable IEC identity must still bind only the exact telegram.
        var thdAfter = Point(
            "runtime-after",
            "THD phase voltage renamed",
            "AA1E1F06R4LD0/MMXU1.ThdPPV.phsA.cVal.mag.f",
            "2.20");
        var cswiAfter = Point(
            "runtime-after",
            "Breaker position renamed",
            "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            "Closed [10]");
        var reordered = new[] { thdAfter, cswiAfter };

        Assert.Equal(thdAfter, reordered[0]);
        Assert.Equal(cswiAfter, reordered[1]);
        Assert.Equal(
            "Open [01]",
            NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, cswiAfter, NativeFatEvidenceField.Value1));
        Assert.Equal(
            string.Empty,
            NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, thdAfter, NativeFatEvidenceField.Value1));
    }

    [Fact]
    public void Arm_DuplicateStableTelegram_FailsClosedWithoutCapturingEitherRow()
    {
        using var coordinator = new NativeFatArmCoordinator();
        var device = Device("runtime-arm-ambiguous");
        device.Points.Add(Point(
            device.DeviceId,
            "CSWI.Pos A",
            "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            "Open [01]"));
        device.Points.Add(Point(
            device.DeviceId,
            "CSWI.Pos B",
            "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            "Closed [10]"));
        var cache = new NativeFatIedSessionCacheState();

        var result = coordinator.Arm(device, cache);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.ArmedRows);
        Assert.False(cache.IsArmed);
        Assert.Empty(cache.EvidenceByRow);
    }

    [Fact]
    public void Arm_MixedUniqueAndAmbiguousRows_ArmsOnlyUniqueStableIdentities()
    {
        using var coordinator = new NativeFatArmCoordinator();
        var device = Device("runtime-arm-mixed");
        var duplicateA = Point(
            device.DeviceId,
            "CSWI.Pos A",
            "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            "Open [01]");
        var duplicateB = Point(
            device.DeviceId,
            "CSWI.Pos B",
            "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            "Closed [10]");
        var unique = Point(
            device.DeviceId,
            "Trip",
            "AA1E1F06R4LD0/PTRC1.Tr.general",
            "False");
        device.Points.Add(duplicateA);
        device.Points.Add(unique);
        device.Points.Add(duplicateB);
        var cache = new NativeFatIedSessionCacheState();

        var result = coordinator.Arm(device, cache);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.ArmedRows);
        Assert.Equal("False", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, unique, NativeFatEvidenceField.Value1));
        Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, duplicateA, NativeFatEvidenceField.Value1));
        Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, duplicateB, NativeFatEvidenceField.Value1));
    }

    [Fact]
    public void P4A_SourceContract_RejectsIndexDisplayAndRuntimeIdentityFallbacks()
    {
        var overlay = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatCanonicalEvidenceOverlay.cs"));
        var arm = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatArmCoordinator.cs"));

        Assert.Contains("point.DeviceName", overlay, StringComparison.Ordinal);
        Assert.Contains("point.IecTelegram", overlay, StringComparison.Ordinal);
        Assert.Contains("Runtime DeviceId, row index, SelectedIndex and display labels are never evidence identity.", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndex", ExtractMethod(overlay, "internal static bool TryBuildRowKey(string? iedName, string? iecTelegram, out string rowKey)"), StringComparison.Ordinal);
        Assert.Contains("identityCount != 1", arm, StringComparison.Ordinal);
        Assert.Contains("SignalName, SelectedIndex or runtime DeviceId", arm, StringComparison.Ordinal);
    }

    private static Iec61850MonitorDevice Device(string deviceId)
        => new()
        {
            DeviceId = deviceId,
            Name = "AA1E1F06R4",
            IpAddress = "192.168.81.103",
            Port = 102,
            IsConnected = true,
            IsMonitoring = true
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