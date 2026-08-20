using System.Text.Json;

namespace ARSAS.Tests;

public sealed class G1ControlCorrectnessRegressionTests
{
    [Fact]
    public void EngineLock_PinsReviewedG24EngineAndPreservesExactG1FieldProvenAncestry()
    {
        var root = RepoRoot();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "engines", "ARIEC61850.lock.json")));
        var json = doc.RootElement;
        Assert.Equal("masarray/ARIEC61850", json.GetProperty("repository").GetString());
        Assert.Equal("main", json.GetProperty("ref").GetString());
        Assert.Equal("609bf51c47f7f404e6e8d9bd338c1380e7a6c1e1", json.GetProperty("commit").GetString());
        Assert.Equal(95, json.GetProperty("sourcePullRequest").GetInt32());
        var purpose = json.GetProperty("purpose").GetString() ?? string.Empty;

        // G2.4 may advance the engine pin only while the field-proven G1/G2.3 ancestry
        // and all of its non-regression safety statements remain explicitly preserved.
        Assert.Contains("a18e550d07f7bbe4ff7753c180b02615075f6292", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("signed primitive constraints", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ordered SBO/SBOw-to-Operate wire evidence", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("object-access-denied", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #94 adds identity-bound qualification profiles", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actual correctly mapped InformationReport", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #95", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transactional URCB TrgOps/OptFlds lease", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("captured byte-for-byte", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact readback", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Production automatic dynamic BRCB/URCB activation remains quarantined", purpose, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalControlPreparationFailure_IsExplicitlyNotSent()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "NativeIec61850Client.cs"));
        Assert.Contains("wireRequestBeforeControl", source, StringComparison.Ordinal);
        Assert.Contains("ControlNotSentFailure", source, StringComparison.Ordinal);
        Assert.Contains("CompletionState = \"NotSent\"", source, StringComparison.Ordinal);
        Assert.Contains("Stage = \"NOT SENT TO IED\"", source, StringComparison.Ordinal);
        Assert.Contains("before any MMS control request was built or sent", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ControlWireUnknownFailure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void App_MapsOrderedNativeControlWireSteps()
    {
        var model = File.ReadAllText(Path.Combine(RepoRoot(), "Models", "ControlModels.cs"));
        var client = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "NativeIec61850Client.cs"));
        Assert.Contains("class Iec61850ControlWireEvidence", model, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<Iec61850ControlWireEvidence> WireSteps", model, StringComparison.Ordinal);
        Assert.Contains("WireSteps = result.WireSteps.Select", client, StringComparison.Ordinal);
        Assert.Contains("Action = step.Action.ToString()", client, StringComparison.Ordinal);
        Assert.Contains("Reference = step.Reference", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_EmitsOrderedWireStepsAndExactRequestResponseEvidence()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("CONTROL_WIRE_STEP:", source, StringComparison.Ordinal);
        Assert.Contains("order={index + 1}", source, StringComparison.Ordinal);
        Assert.Contains("action={step.Action}", source, StringComparison.Ordinal);
        Assert.Contains("reference={step.Reference}", source, StringComparison.Ordinal);
        Assert.Contains("CONTROL_WIRE_REQUEST:", source, StringComparison.Ordinal);
        Assert.Contains("CONTROL_WIRE_RESPONSE:", source, StringComparison.Ordinal);
        Assert.Contains("ordered MMS control response(s) captured", source, StringComparison.Ordinal);
        Assert.Contains("Never infer server acceptance from request HEX alone", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MMS command submitted", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandUi_DistinguishesNotSentAndShowsWireSequence()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "ControlCommandWindow.xaml.cs"));
        Assert.Contains("No MMS control request was sent to the IED", source, StringComparison.Ordinal);
        Assert.Contains("MMS request/response wire evidence was captured", source, StringComparison.Ordinal);
        Assert.Contains("MMS request encoding was captured, but no MMS response was captured", source, StringComparison.Ordinal);
        Assert.Contains("Wire sequence:", source, StringComparison.Ordinal);
        Assert.Contains("result.WireSteps.Select(step => step.Action)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void G11_FieldRejection_IsExplicitAndManualOriginIsStationControl()
    {
        var model = File.ReadAllText(Path.Combine(RepoRoot(), "Models", "ControlModels.cs"));
        var dialog = File.ReadAllText(Path.Combine(RepoRoot(), "ControlCommandWindow.xaml.cs"));
        var main = File.ReadAllText(Path.Combine(RepoRoot(), "MainWindow.xaml.cs"));
        var client = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "NativeIec61850Client.cs"));
        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));

        Assert.Contains("OriginCategory { get; init; } = \"StationControl\"", model, StringComparison.Ordinal);
        Assert.Contains("OriginCategory = \"StationControl\"", dialog, StringComparison.Ordinal);
        Assert.Contains("OriginCategory = \"StationControl\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginCategory = \"Maintenance\"", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginCategory = \"Maintenance\"", main, StringComparison.Ordinal);
        Assert.Contains("Iec61850OriginCategory.StationControl", client, StringComparison.Ordinal);
        Assert.DoesNotContain("MMS command submitted:", main, StringComparison.Ordinal);
        Assert.Contains("wire send is not assumed until native evidence is returned", main, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rejectedStep.Action.Equals(\"SelectWithValue\"", dialog, StringComparison.Ordinal);
        Assert.Contains("? \"SBOw\" : rejectedStep.Action", dialog, StringComparison.Ordinal);
        Assert.Contains("IED REJECTED {rejectedStage}", dialog, StringComparison.Ordinal);
        Assert.Contains("Operate was NOT sent because SBOw selection failed", dialog, StringComparison.Ordinal);
        Assert.Contains("IED BLOCKED COMMAND BY INTERLOCKING", dialog, StringComparison.Ordinal);
        Assert.Contains("IED BLOCKED COMMAND BY SYNCHROCHECK", dialog, StringComparison.Ordinal);
        Assert.Contains("requested control condition/service is not supported", dialog, StringComparison.Ordinal);
        Assert.Contains("CONTROL_REJECTED_BY_IED:", runtime, StringComparison.Ordinal);
        Assert.Contains("Control execution requested:", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("Control intent accepted:", runtime, StringComparison.Ordinal);
        Assert.Contains("OperateSent={operateSent}", runtime, StringComparison.Ordinal);
        Assert.Contains("origin={request.OriginCategory}/{request.Originator}", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("RetryControl", runtime, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G1_DoesNotReenableDynamicReportingOrChangeReconnectPolicy()
    {
        var engineLock = File.ReadAllText(Path.Combine(RepoRoot(), "engines", "ARIEC61850.lock.json"));
        Assert.Contains("PR #89 quarantines automatic full dynamic DataSet activation", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Production automatic dynamic BRCB/URCB activation remains quarantined", engineLock, StringComparison.OrdinalIgnoreCase);

        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("SmartReconnectPolicy", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicBrcb = true", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicUrcb = true", runtime, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
