using System.Diagnostics;
using System.Text.Json.Nodes;

namespace ARSAS.Tests;

/// <summary>
/// Executes the actual embedded CI PowerShell gate using disposable lock/evidence
/// fixtures: accepted trials are never interpreted as mainline physical proof.
/// </summary>
public sealed class P10MainlinePolicyExecutionTests
{
    [Theory]
    [InlineData("p10-stacked-valid", true)]
    [InlineData("core-trial-wrong-exact-sha-denied", false)]
    [InlineData("core-trial-wrong-base-denied", false)]
    [InlineData("core-trial-wrong-ci-denied", false)]
    [InlineData("core-trial-physical-flag-denied", false)]
    [InlineData("subscriber-trial-sha-denied", false)]
    [InlineData("subscriber-trial-parent-denied", false)]
    [InlineData("subscriber-trial-ci-denied", false)]
    [InlineData("subscriber-trial-physical-flag-denied", false)]
    [InlineData("subscriber-trial-mainline-flag-denied", false)]
    [InlineData("canonical-trial-sha-denied", false)]
    [InlineData("canonical-trial-parent-denied", false)]
    [InlineData("canonical-trial-ci-denied", false)]
    [InlineData("canonical-trial-physical-flag-denied", false)]
    [InlineData("canonical-trial-mainline-flag-denied", false)]
    [InlineData("canonical-trial-polling-flag-denied", false)]
    [InlineData("ziv-trial-sha-denied", false)]
    [InlineData("ziv-trial-base-denied", false)]
    [InlineData("ziv-trial-ci-denied", false)]
    [InlineData("ziv-trial-physical-denied", false)]
    [InlineData("ziv-trial-mainline-denied", false)]
    [InlineData("ziv-trial-polling-denied", false)]
    [InlineData("ziv-trial-bsc-gate-denied", false)]
    [InlineData("generic-trial-sha-denied", false)]
    [InlineData("generic-trial-parent-denied", false)]
    [InlineData("generic-trial-ci-denied", false)]
    [InlineData("generic-trial-semantic-gate-denied", false)]
    [InlineData("generic-trial-inventory-gate-denied", false)]
    [InlineData("generic-trial-vendor-denied", false)]
    [InlineData("p10-pr-main-denied", false)]
    [InlineData("p10-source-main-denied", false)]
    [InlineData("p10-wrong-sha-denied", false)]
    [InlineData("p10-wrong-parent-denied", false)]
    [InlineData("p10-wrong-source-pr-denied", false)]
    [InlineData("p10-wrong-identity-source-denied", false)]
    [InlineData("p10-cyclic-fallback-denied", false)]
    [InlineData("p10-history-tampered-denied", false)]
    [InlineData("p10-dataset-lineage-tampered-denied", false)]
    [InlineData("accepted-physical-baseline", true)]
    public void Exact_CI_Gate_Fails_Closed_Without_False_Physical_Promotion(
        string scenario, bool shouldPass)
    {
        if (!OperatingSystem.IsWindows())
            return; // Windows Actions runners use built-in Windows PowerShell 5.1.

        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root,
            ".github", "workflows", "smart-discovery-mainline-readiness.yml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        const string step = "      - name: Verify R10 physical convergence authority\n";
        const string run = "        run: |\n";
        const string next = "\n      - name: Checkout exact accepted ARIEC61850 integration authority";
        var stepStart = workflow.IndexOf(step, StringComparison.Ordinal);
        Assert.True(stepStart >= 0, "R10 physical verification step must exist.");
        var scriptStart = workflow.IndexOf(run, stepStart, StringComparison.Ordinal);
        Assert.True(scriptStart >= 0, "R10 verification step must have a run script.");
        scriptStart += run.Length;
        var scriptEnd = workflow.IndexOf(next, scriptStart, StringComparison.Ordinal);
        Assert.True(scriptEnd > scriptStart, "R10 script boundary must be explicit.");
        var script = string.Join("\n", workflow[scriptStart..scriptEnd].Split('\n')
            .Select(line => line.StartsWith("          ", StringComparison.Ordinal)
                ? line[10..] : line));

        var folder = Path.Combine(Path.GetTempPath(),
            "arsas-p10-mainline-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "engines"));
            Directory.CreateDirectory(Path.Combine(folder, "evidence"));
            Directory.CreateDirectory(Path.Combine(folder, "scripts"));
            File.Copy(Path.Combine(root, "scripts", "test-p10-9f-core-routing-trial.ps1"),
                Path.Combine(folder, "scripts", "test-p10-9f-core-routing-trial.ps1"));
            var lockDoc = JsonNode.Parse(File.ReadAllText(Path.Combine(root,
                "engines", "ARIEC61850.lock.json")))!.AsObject();

            string baseRef = "fix/512-goose-event-header-alignment";
            string sourceRef = "512/merge";
            switch (scenario)
            {
                case "p10-pr-main-denied": baseRef = "main"; break;
                case "core-trial-wrong-exact-sha-denied":
                    lockDoc["coreReportRoutingTrial"]!["exactCommit"] = new string('0', 40); break;
                case "core-trial-wrong-base-denied":
                    lockDoc["coreReportRoutingTrial"]!["baseEngineCommit"] = new string('0', 40); break;
                case "core-trial-wrong-ci-denied":
                    lockDoc["coreReportRoutingTrial"]!["coreCiRun"] = 0; break;
                case "core-trial-physical-flag-denied":
                    lockDoc["coreReportRoutingTrial"]!["physicalQualificationRequired"] = false; break;
                case "subscriber-trial-sha-denied":
                    lockDoc["reportSubscriberIntegrityTrial"]!["exactCommit"] = new string('0', 40); break;
                case "subscriber-trial-parent-denied":
                    lockDoc["reportSubscriberIntegrityTrial"]!["baseEngineCommit"] = new string('0', 40); break;
                case "subscriber-trial-ci-denied":
                    lockDoc["reportSubscriberIntegrityTrial"]!["engineCiRun"] = 0; break;
                case "subscriber-trial-physical-flag-denied":
                    lockDoc["reportSubscriberIntegrityTrial"]!["physicalQualificationRequired"] = false; break;
                case "subscriber-trial-mainline-flag-denied":
                    lockDoc["reportSubscriberIntegrityTrial"]!["mainlinePromotionForbidden"] = false; break;
                case "canonical-trial-sha-denied":
                    lockDoc["canonicalReportIngressIntegrityTrial"]!["exactCommit"] = new string('0', 40); break;
                case "canonical-trial-parent-denied":
                    lockDoc["canonicalReportIngressIntegrityTrial"]!["baseEngineCommit"] = new string('0', 40); break;
                case "canonical-trial-ci-denied":
                    lockDoc["canonicalReportIngressIntegrityTrial"]!["engineCiRun"] = 0; break;
                case "canonical-trial-physical-flag-denied":
                    lockDoc["canonicalReportIngressIntegrityTrial"]!["physicalQualificationRequired"] = false; break;
                case "canonical-trial-mainline-flag-denied":
                    lockDoc["canonicalReportIngressIntegrityTrial"]!["mainlinePromotionForbidden"] = false; break;
                case "canonical-trial-polling-flag-denied":
                    lockDoc["canonicalReportIngressIntegrityTrial"]!["cyclicMmsProcessPollingForbidden"] = false; break;
                case "ziv-trial-sha-denied":
                    lockDoc["zivSclBscInteroperabilityTrial"]!["exactCommit"] = new string('0', 40); break;
                case "ziv-trial-base-denied":
                    lockDoc["zivSclBscInteroperabilityTrial"]!["baseEngineCommit"] = new string('0', 40); break;
                case "ziv-trial-ci-denied":
                    lockDoc["zivSclBscInteroperabilityTrial"]!["engineCiRun"] = 0; break;
                case "ziv-trial-physical-denied":
                    lockDoc["zivSclBscInteroperabilityTrial"]!["physicalQualificationRequired"] = false; break;
                case "ziv-trial-mainline-denied":
                    lockDoc["zivSclBscInteroperabilityTrial"]!["mainlinePromotionForbidden"] = false; break;
                case "ziv-trial-polling-denied":
                    lockDoc["zivSclBscInteroperabilityTrial"]!["cyclicMmsProcessPollingForbidden"] = false; break;
                case "ziv-trial-bsc-gate-denied":
                    lockDoc["zivSclBscInteroperabilityTrial"]!["bscLiveSemanticGateRequired"] = false; break;
                case "generic-trial-sha-denied":
                    lockDoc["genericControlInteroperabilityTrial"]!["exactCommit"] = new string('0', 40); break;
                case "generic-trial-parent-denied":
                    lockDoc["genericControlInteroperabilityTrial"]!["baseEngineCommit"] = new string('0', 40); break;
                case "generic-trial-ci-denied":
                    lockDoc["genericControlInteroperabilityTrial"]!["engineCiRun"] = 0; break;
                case "generic-trial-semantic-gate-denied":
                    lockDoc["genericControlInteroperabilityTrial"]!["typedSemanticGateRequired"] = false; break;
                case "generic-trial-inventory-gate-denied":
                    lockDoc["genericControlInteroperabilityTrial"]!["associationScopedInventoryRequired"] = false; break;
                case "generic-trial-vendor-denied":
                    lockDoc["genericControlInteroperabilityTrial"]!["vendorDispatchForbidden"] = false; break;
                case "p10-source-main-denied": sourceRef = "main"; break;
                case "p10-wrong-sha-denied":
                    lockDoc["p10IdentityTrial"]!["exactCommit"] = new string('0', 40); break;
                case "p10-wrong-parent-denied":
                    lockDoc["p10IdentityTrial"]!["baseEngineCommit"] = new string('0', 40); break;
                case "p10-wrong-source-pr-denied":
                    lockDoc["sourcePullRequest"] = 160; break;
                case "p10-wrong-identity-source-denied":
                    lockDoc["p10IdentityTrial"]!["identitySource"] = "HostFallback"; break;
                case "p10-cyclic-fallback-denied":
                    lockDoc["p10IdentityTrial"]!["cyclicMmsProcessPollingForbidden"] = false; break;
                case "p10-history-tampered-denied":
                    lockDoc["mergedMainCommit"] = new string('0', 40); break;
                case "p10-dataset-lineage-tampered-denied":
                    lockDoc["datasetFirstDiscoveryTrial"]!["exactCommit"] = new string('0', 40); break;
                case "accepted-physical-baseline":
                    lockDoc["commit"] = "84e9820e5a32690475960e49d5ef74e6637847fd";
                    lockDoc["sourcePullRequest"] = 143;
                    baseRef = "main";
                    break;
            }

            File.WriteAllText(Path.Combine(folder, "engines", "ARIEC61850.lock.json"),
                lockDoc.ToJsonString());
            File.Copy(Path.Combine(root, "evidence", "interoperability-reference-target.json"),
                Path.Combine(folder, "evidence", "interoperability-reference-target.json"));
            var scriptPath = Path.Combine(folder, "verify-r10.ps1");
            File.WriteAllText(scriptPath, script);
            var environment = Path.Combine(folder, "github-env.txt");
            File.WriteAllText(environment, "");
            var info = new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = folder,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive",
                "-ExecutionPolicy", "Bypass", "-File", scriptPath })
                info.ArgumentList.Add(arg);
            info.Environment["PR_BASE_REF"] = baseRef;
            info.Environment["SOURCE_REF_NAME"] = sourceRef;
            info.Environment["GITHUB_ENV"] = environment;

            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() +
                         process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("CI gate fixture exceeded 30 seconds.");
            }

            if (shouldPass)
            {
                Assert.True(process.ExitCode == 0,
                    $"Expected acceptance for {scenario}, exit {process.ExitCode}:\n{output}");
                Assert.Contains("ENGINE_COMMIT=", File.ReadAllText(environment));
            }
            else
            {
                Assert.NotEqual(0, process.ExitCode);
                Assert.DoesNotContain("ENGINE_COMMIT=", File.ReadAllText(environment));
            }
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName,
                "engines", "ARIEC61850.lock.json")) &&
                File.Exists(Path.Combine(current.FullName, ".github",
                    "workflows", "smart-discovery-mainline-readiness.yml")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
