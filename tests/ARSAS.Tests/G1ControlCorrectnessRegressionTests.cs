using System.Text.Json;

namespace ARSAS.Tests;

public sealed class G1ControlCorrectnessRegressionTests
{
    [Fact]
    public void EngineLock_PinsGuardedRuntimeEngineAndPreservesExactG1FieldProvenAncestry()
    {
        var root = RepoRoot();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "engines", "ARIEC61850.lock.json")));
        var json = doc.RootElement;
        Assert.Equal("masarray/ARIEC61850", json.GetProperty("repository").GetString());
        Assert.Equal("main", json.GetProperty("ref").GetString());
        Assert.Equal("0965f67fe912355b3b29fc8123872a68d4064b04", json.GetProperty("commit").GetString());
        Assert.Equal(102, json.GetProperty("sourcePullRequest").GetInt32());
        var purpose = json.GetProperty("purpose").GetString() ?? string.Empty;

        // G2.6 may advance the engine pin only while the field-proven G1/G2.3/P0/P1 ancestry
        // and all non-regression reporting/control safety statements remain explicit.
        Assert.Contains("a18e550d07f7bbe4ff7753c180b02615075f6292", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("signed primitive constraints", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ordered SBO/SBOw-to-Operate wire evidence", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("object-access-denied", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #94 adds identity-bound qualification profiles", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actual correctly mapped InformationReport", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #95", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bit 0 reserved", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bits 1..5 dchg/qchg/dupd/integrity/GI", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dchg+GI encodes canonically as 0244", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrgOps-only micro-probe", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("P1 adds a dedicated one-URCB OptFlds-only", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canonical target 061800", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never writing TrgOps, DatSet, Resv, RptEna, GI", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact local TCP address", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("C0A851F0", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("192.168.81.240", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Owner mismatch or unsupported encoding remains a hard failure", purpose, StringComparison.OrdinalIgnoreCase);

        // PR #97 adds the ProductionEligible consumer, PR #98/#99 preserve strict
        // certification evidence, PR #100 adds guarded runtime, PR #101 adds the exact
        // legacy adapter, and PR #102 narrows the real broader chain to its physical dchg subset.
        Assert.Contains("PR #97", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible profile", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact InformationReport-proven RCB/member evidence", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #98", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report-vs-independent-MMS shadow evaluator", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never mutates a profile", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #99", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actually observed paired report/poll quality evidence", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actually observed paired report/poll device timestamp evidence", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absence of q/t evidence cannot become a production PASS", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #100", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("identity-compatible InformationReportProven", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at most one exact proven dynamic RCB/member envelope", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not call MarkProductionEligible", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible as a separate certification boundary", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #101", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #102", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact ordered subset", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no profile save/mutation", purpose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never authorizes ProductionEligible", purpose, StringComparison.OrdinalIgnoreCase);
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
    public void G1_ControlPathDoesNotOwnGuardedDynamicRuntimeOrChangeReconnectPolicy()
    {
        var engineLock = File.ReadAllText(Path.Combine(RepoRoot(), "engines", "ARIEC61850.lock.json"));
        Assert.Contains("PR #89 quarantines automatic full dynamic DataSet activation", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #100", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #101", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #102", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible as a separate certification boundary", engineLock, StringComparison.OrdinalIgnoreCase);

        // G1 control remains independent from the G2.6 report acquisition bridge.
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
