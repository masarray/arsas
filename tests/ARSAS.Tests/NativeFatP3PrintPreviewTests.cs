using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatP3PrintPreviewTests
{
    [Fact]
    public void Capture_CopiesSelectedCanonicalRowsInCurrentOrderAndSparseEvidence()
    {
        var device = Device("dev-aa1e1f06r4", "AA1E1F06R4", "192.168.81.103");
        var breaker = Point(device.DeviceId, device.Name, "Breaker", "AA1E1F06R4LD0/XCBR1.Pos.stVal", "Open [01]");
        var trip = Point(device.DeviceId, device.Name, "Trip", "AA1E1F06R4LD0/GGIO1.Ind1.stVal", "False");
        device.Points.Add(breaker);
        device.Points.Add(trip);

        var cache = new NativeFatIedSessionCacheState();
        NativeFatCanonicalEvidenceOverlay.Write(cache, breaker, NativeFatEvidenceField.Value1, "Open [01]");
        NativeFatCanonicalEvidenceOverlay.Write(cache, breaker, NativeFatEvidenceField.Value2, "Closed [10]");
        NativeFatCanonicalEvidenceOverlay.Write(cache, breaker, NativeFatEvidenceField.Result, "PASS");
        NativeFatCanonicalEvidenceOverlay.Write(cache, trip, NativeFatEvidenceField.Value1, "False");

        var snapshot = NativeFatPrintPreviewSnapshot.Capture(device, cache);

        Assert.Equal(device.DeviceId, snapshot.DeviceId);
        Assert.Equal("AA1E1F06R4", snapshot.IedName);
        Assert.Equal("192.168.81.103", snapshot.IpAddress);
        Assert.Equal(102, snapshot.Port);
        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal("Breaker", snapshot.Rows[0].Signal);
        Assert.Equal("Trip", snapshot.Rows[1].Signal);
        Assert.Equal("LD0/XCBR1.Pos.stVal", snapshot.Rows[0].IecTelegram);
        Assert.Equal("Good", snapshot.Rows[0].Quality);
        Assert.Equal("Open [01]", snapshot.Rows[0].LiveValue);
        Assert.Equal("Open [01]", snapshot.Rows[0].Value1);
        Assert.NotEqual("—", snapshot.Rows[0].Value1TimestampText);
        Assert.Equal("Closed [10]", snapshot.Rows[0].Value2);
        Assert.NotEqual("—", snapshot.Rows[0].Value2TimestampText);
        Assert.Equal("PASS", snapshot.Rows[0].Result);
        Assert.Equal("1/2 complete", snapshot.ProgressText);
    }

    [Fact]
    public void Capture_RemainsImmutableWhenLiveRowsAndEvidenceChange()
    {
        var device = Device("dev-aa1e1f06r4", "AA1E1F06R4", "192.168.81.103");
        var point = Point(device.DeviceId, device.Name, "Breaker", "AA1E1F06R4LD0/XCBR1.Pos.stVal", "Open [01]");
        device.Points.Add(point);
        var cache = new NativeFatIedSessionCacheState();
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value1, "Open [01]");
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value2, "Closed [10]");
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Result, "PASS");

        var snapshot = NativeFatPrintPreviewSnapshot.Capture(device, cache);
        var capturedValue1 = snapshot.Rows[0].Value1;
        var capturedValue1Timestamp = snapshot.Rows[0].Value1TimestampText;
        var capturedValue2 = snapshot.Rows[0].Value2;
        var capturedValue2Timestamp = snapshot.Rows[0].Value2TimestampText;

        point.Value = "Closed [10]";
        point.SignalName = "MUTATED";
        point.Quality = "Questionable";
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value1, "NEW-V1");
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value2, "NEW-V2");
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Result, "REVIEW");

        Assert.Single(snapshot.Rows);
        Assert.Equal("Breaker", snapshot.Rows[0].Signal);
        Assert.Equal("LD0/XCBR1.Pos.stVal", snapshot.Rows[0].IecTelegram);
        Assert.Equal("Good", snapshot.Rows[0].Quality);
        Assert.Equal("Open [01]", snapshot.Rows[0].LiveValue);
        Assert.Equal(capturedValue1, snapshot.Rows[0].Value1);
        Assert.Equal(capturedValue1Timestamp, snapshot.Rows[0].Value1TimestampText);
        Assert.Equal(capturedValue2, snapshot.Rows[0].Value2);
        Assert.Equal(capturedValue2Timestamp, snapshot.Rows[0].Value2TimestampText);
        Assert.Equal("Open [01]", snapshot.Rows[0].Value1);
        Assert.Equal("Closed [10]", snapshot.Rows[0].Value2);
        Assert.Equal("PASS", snapshot.Rows[0].Result);
    }

    [Fact]
    public void Capture_ContainsOnlyRequestedDevice()
    {
        var selected = Device("dev-aa1e1f06r4", "AA1E1F06R4", "192.168.81.103");
        selected.Points.Add(Point(selected.DeviceId, selected.Name, "Selected", "AA1E1F06R4LD0/GGIO1.Ind1.stVal", "True"));

        var other = Device("dev-other", "OTHER-IED", "192.168.81.104");
        other.Points.Add(Point(other.DeviceId, other.Name, "Other", "OTHERLD0/GGIO1.Ind1.stVal", "False"));

        var snapshot = NativeFatPrintPreviewSnapshot.Capture(selected, new NativeFatIedSessionCacheState());

        Assert.Equal(selected.DeviceId, snapshot.DeviceId);
        Assert.Equal(selected.Name, snapshot.IedName);
        Assert.Single(snapshot.Rows);
        Assert.Equal("Selected", snapshot.Rows[0].Signal);
        Assert.DoesNotContain(snapshot.Rows, row => row.Signal == "Other");
    }

    [Fact]
    public void P3_PreviewCaptureRemainsLazySelectedIedOnly()
    {
        var gridSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatCanonicalGrid.cs"));
        var previewSource = File.ReadAllText(FindRepoFile("MainWindow.NativeFatPrintPreview.cs"));
        var snapshotSource = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatPrintPreviewSnapshot.cs"));

        var build = ExtractMethod(gridSource, "private FrameworkElement BuildNativeFatCanonicalWorkspace(string? statusText = null)");
        var bind = ExtractMethod(gridSource, "private void BindNativeFatCanonicalRows()");
        var click = ExtractMethod(previewSource, "private void NativeFatPrintPreviewButton_Click(object sender, RoutedEventArgs e)");

        Assert.Contains("Content = \"Print Preview\"", build, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeFatPrintPreviewSnapshot.Capture", build, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowNativeFatPrintPreview", build, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeFatPrintPreviewSnapshot.Capture", bind, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowNativeFatPrintPreview", bind, StringComparison.Ordinal);
        Assert.Contains("NativeFatPrintPreviewSnapshot.Capture", click, StringComparison.Ordinal);
        Assert.Contains("ShowNativeFatPrintPreview(snapshot)", click, StringComparison.Ordinal);
        Assert.Contains("SelectedDevice", click, StringComparison.Ordinal);

        Assert.Contains("device.Points.Select", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("Array.AsReadOnly", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("Copy(point.IecTelegram)", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("Copy(point.Quality)", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("NativeFatCanonicalEvidenceOverlay.ReadRaw", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("NativeFatCanonicalEvidenceOverlay.ReadCapture", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("DisplayTimestamp(capture1)", snapshotSource, StringComparison.Ordinal);
        Assert.Contains("DisplayTimestamp(capture2)", snapshotSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850MonitorPoint Point", snapshotSource, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     "IoTestProject",
                     "PrepareIoTestIedForFatAsync",
                     "OpenDescribedSourcesAsync",
                     "IoFatEngineeringWorkspaceProjectionService",
                     "ConnectAndDiscoverAsync",
                     "StartMonitoringAsync"
                 })
        {
            Assert.DoesNotContain(forbidden, previewSource, StringComparison.Ordinal);
            Assert.DoesNotContain(forbidden, snapshotSource, StringComparison.Ordinal);
        }
    }

    private static Iec61850MonitorDevice Device(string deviceId, string name, string ip)
        => new()
        {
            DeviceId = deviceId,
            Name = name,
            IpAddress = ip,
            Port = 102,
            IsConnected = true,
            IsMonitoring = true
        };

    private static Iec61850MonitorPoint Point(
        string deviceId,
        string deviceName,
        string signalName,
        string reference,
        string value)
        => new()
        {
            DeviceId = deviceId,
            DeviceName = deviceName,
            SignalName = signalName,
            IecReference = reference,
            IecDataType = "BOOLEAN",
            Quality = "Good",
            Status = "Live",
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