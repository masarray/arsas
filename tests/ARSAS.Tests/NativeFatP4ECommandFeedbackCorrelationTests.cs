using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatP4ECommandFeedbackCorrelationTests
{
    [Fact]
    public void ExactStatusReference_ResolvesCanonicalFeedbackAndNotUnrelatedThdRow()
    {
        var device = Device("AA1E1F06R4");
        var thd = Point(device, "THD", "AA1E1F06R4LD0/MMXU1.ThdPPV.phsA.cVal.mag.f");
        var feedback = Point(device, "Breaker feedback", "AA1E1F06R4LD0/CSWI1.Pos.stVal");
        device.Points.Add(thd);
        device.Points.Add(feedback);
        var capabilities = new Iec61850ControlCapabilities
        {
            ObjectReference = "AA1E1F06R4LD0/CSWI1.Pos.Oper",
            StatusReference = "AA1E1F06R4LD0/CSWI1.Pos.stVal",
            ControlModel = Iec61850ControlModelKind.DirectNormal,
            EngineControlServiceAvailable = true,
            IsOperationallyReady = true
        };

        var resolved = NativeFatCommandFeedbackCorrelation.Resolve(device, capabilities);

        Assert.Same(feedback, resolved);
        Assert.NotSame(thd, resolved);
    }

    [Fact]
    public void MissingStatusReference_FailsClosedWithoutObjectReferenceFallback()
    {
        var device = Device("AA1E1F06R4");
        device.Points.Add(Point(device, "Breaker feedback", "AA1E1F06R4LD0/CSWI1.Pos.stVal"));
        var capabilities = new Iec61850ControlCapabilities
        {
            ObjectReference = "AA1E1F06R4LD0/CSWI1.Pos.Oper",
            StatusReference = string.Empty,
            ControlModel = Iec61850ControlModelKind.DirectNormal,
            EngineControlServiceAvailable = true,
            IsOperationallyReady = true
        };

        Assert.Null(NativeFatCommandFeedbackCorrelation.Resolve(device, capabilities));
    }

    [Fact]
    public void DuplicateCanonicalFeedbackIdentity_FailsClosedInsteadOfChoosingByOrder()
    {
        var device = Device("AA1E1F06R4");
        device.Points.Add(Point(device, "Feedback A", "AA1E1F06R4LD0/CSWI1.Pos.stVal"));
        device.Points.Add(Point(device, "Feedback B", "AA1E1F06R4LD0/CSWI1.Pos.stVal"));

        Assert.Null(NativeFatCommandFeedbackCorrelation.Resolve(
            device,
            "AA1E1F06R4LD0/CSWI1.Pos.stVal"));
    }

    [Fact]
    public void SameSignalNameOnDifferentTelegram_CannotInfluenceCorrelation()
    {
        var device = Device("AA1E1F06R4");
        var wrong = Point(device, "Breaker position", "AA1E1F06R4LD0/GGIO1.Ind1.stVal");
        var expected = Point(device, "Breaker position", "AA1E1F06R4LD0/CSWI1.Pos.stVal");
        device.Points.Add(wrong);
        device.Points.Add(expected);

        var resolved = NativeFatCommandFeedbackCorrelation.Resolve(
            device,
            "AA1E1F06R4LD0/CSWI1.Pos.stVal");

        Assert.Same(expected, resolved);
    }

    [Fact]
    public void StatusReferenceFromAnotherIed_DoesNotCrossContaminateCurrentDevice()
    {
        var device = Device("IED_A");
        device.Points.Add(Point(device, "Breaker feedback", "IED_ALD0/CSWI1.Pos.stVal"));

        Assert.Null(NativeFatCommandFeedbackCorrelation.Resolve(
            device,
            "IED_BLD0/CSWI1.Pos.stVal"));
    }

    [Fact]
    public void SourceContract_HasNoDisplayIndexOrRuntimeIdentityFallback()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/NativeFatCommandFeedbackCorrelation.cs"));

        Assert.Contains("capabilities.StatusReference", source, StringComparison.Ordinal);
        Assert.Contains("NativeFatCanonicalEvidenceOverlay.TryBuildRowKey", source, StringComparison.Ordinal);
        Assert.Contains("if (match != null)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedIndex", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.IndexOf", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeviceId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectReference", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalStableCommandConfirmation_AlsoRequiresExplicitControlStatusReference()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ControlDiagnostics.cs"));
        var start = source.IndexOf("private static string ResolveControlFeedbackKey", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task ExpirePositionCommandAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        var resolver = source[start..end];
        Assert.Contains("string.IsNullOrWhiteSpace(signal.ControlStatusReference)", resolver, StringComparison.Ordinal);
        Assert.Contains("return string.Empty;", resolver, StringComparison.Ordinal);
        Assert.Contains("NormalizeReference(signal.ControlStatusReference)", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("signal.ObjectReference", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("SignalName)", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("signal.ObjectReference +", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"{signal.ObjectReference}.stVal\"", resolver, StringComparison.Ordinal);
    }

    private static Iec61850MonitorDevice Device(string name)
        => new()
        {
            DeviceId = "runtime-" + name,
            Name = name,
            IpAddress = "192.168.81.103",
            Port = 102
        };

    private static Iec61850MonitorPoint Point(
        Iec61850MonitorDevice device,
        string signalName,
        string reference)
        => new()
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            SignalName = signalName,
            IecReference = reference,
            IecDataType = "BOOLEAN",
            Quality = "Good",
            Value = "False"
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
