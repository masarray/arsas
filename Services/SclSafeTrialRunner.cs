using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed record SclSafeTrialCommand(
    string SclPath,
    string IedName,
    string AccessPointName,
    string Host,
    int Port,
    int MaximumVariableReferencesPerRead,
    string EvidencePath)
{
    public const string Switch = "--scl-safe-trial";
    public const string SingleReferenceSwitch = "--scl-safe-trial-single";

    public static bool IsRequested(IReadOnlyList<string> args)
        => args.Any(argument =>
            string.Equals(argument, Switch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, SingleReferenceSwitch, StringComparison.OrdinalIgnoreCase));

    public static bool TryParse(IReadOnlyList<string> args, out SclSafeTrialCommand? command, out string error)
    {
        command = null;
        error = string.Empty;
        if (args.Count == 0 ||
            (!string.Equals(args[0], Switch, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(args[0], SingleReferenceSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Expected {Switch} or {SingleReferenceSwitch} as the first argument.";
            return false;
        }

        if (args.Count < 5)
        {
            error =
                $"Usage: ARSAS.exe {Switch} <SCL/CID path> <IED name> <AccessPoint name> <host/IP> [port] [evidence JSON path]. " +
                $"Use {SingleReferenceSwitch} with the same arguments for a controlled one-variable-per-Read interoperability trial.";
            return false;
        }

        var sclPath = Path.GetFullPath(args[1]);
        var iedName = (args[2] ?? string.Empty).Trim();
        var accessPointName = (args[3] ?? string.Empty).Trim();
        var host = (args[4] ?? string.Empty).Trim();
        var port = 102;
        if (args.Count >= 6 && (!int.TryParse(args[5], out port) || port is < 1 or > 65535))
        {
            error = $"Invalid MMS TCP port '{args[5]}'; expected 1..65535.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(iedName) || string.IsNullOrWhiteSpace(accessPointName) || string.IsNullOrWhiteSpace(host))
        {
            error = "IED name, AccessPoint name and host/IP are required.";
            return false;
        }

        var maximumVariableReferencesPerRead = string.Equals(
            args[0],
            SingleReferenceSwitch,
            StringComparison.OrdinalIgnoreCase)
            ? 1
            : ArMms.MmsReadBatchCodec.MaximumVariableReferencesPerRead;

        var evidencePath = args.Count >= 7 && !string.IsNullOrWhiteSpace(args[6])
            ? Path.GetFullPath(args[6])
            : Path.Combine(
                Path.GetTempPath(),
                $"ARSAS-SCL-Trial-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{maximumVariableReferencesPerRead}ref-{Guid.NewGuid():N}.json");

        command = new SclSafeTrialCommand(
            sclPath,
            iedName,
            accessPointName,
            host,
            port,
            maximumVariableReferencesPerRead,
            evidencePath);
        return true;
    }
}

public sealed record SclSafeTrialRunResult(
    int ExitCode,
    bool IsSuccess,
    string Message,
    string EvidencePath);

/// <summary>
/// Read-only laboratory entry point for physically validating the SCL-assisted MMS path.
/// It performs association, Domain/VMD reconciliation and bounded initial FC-root Reads only.
/// The process exits immediately afterwards, so reporting, control, writes and hidden full
/// discovery cannot be entered by this trial path.
/// </summary>
public static class SclSafeTrialRunner
{
    public static async Task<SclSafeTrialRunResult> RunAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        if (!SclSafeTrialCommand.TryParse(args, out var command, out var parseError) || command is null)
            return await WriteInputFailureAsync(args, parseError, cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        string sourceSha256 = string.Empty;
        SclAssistedClientConnectResult? result = null;
        Iec61850DeviceDiagnosticSnapshot? diagnostic = null;
        string message;
        var exitCode = 0;

        try
        {
            if (!File.Exists(command.SclPath))
                throw new FileNotFoundException("SCL/CID source file was not found.", command.SclPath);

            var sourceBytes = await File.ReadAllBytesAsync(command.SclPath, cancellationToken).ConfigureAwait(false);
            sourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();

            // Let an XML parser honor the document encoding and normalize only the in-memory
            // representation passed to the pure SCL planners. The evidence hash remains over
            // the exact source bytes selected by the operator.
            var document = XDocument.Load(command.SclPath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var sclXml = document.ToString(SaveOptions.DisableFormatting);

            var client = new NativeIec61850Client();
            result = await client.ConnectUsingSclAsync(
                sclXml,
                command.IedName,
                command.AccessPointName,
                command.Host,
                command.Port,
                command.MaximumVariableReferencesPerRead,
                cancellationToken).ConfigureAwait(false);

            diagnostic = client.CaptureDiagnosticSnapshot("SCL safe trial");
            message = result.Message;
            exitCode = result.IsSuccess ? 0 : 31;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            message = "SCL safe trial was cancelled by the operator.";
            exitCode = 32;
        }
        catch (Exception ex)
        {
            message = $"SCL safe trial failed before completion: {ex.GetType().Name}: {ex.Message}";
            exitCode = 33;
        }
        finally
        {
            stopwatch.Stop();
        }

        var evidence = new
        {
            schema = "arsas-scl-safe-trial-v2",
            capturedAtUtc = DateTimeOffset.UtcNow,
            safety = new
            {
                readOnly = true,
                fullDiscoveryAllowed = false,
                writesAllowed = false,
                controlAllowed = false,
                reportEnableAllowed = false,
                dynamicDataSetAllowed = false,
                automaticReadFallbackAllowed = false
            },
            source = new
            {
                path = command.SclPath,
                sha256 = sourceSha256,
                iedName = command.IedName,
                accessPointName = command.AccessPointName,
                host = command.Host,
                port = command.Port
            },
            trialMode = new
            {
                maximumVariableReferencesPerRead = command.MaximumVariableReferencesPerRead,
                label = command.MaximumVariableReferencesPerRead == 1
                    ? "single-reference-control"
                    : "iedscout-like-bounded-batch"
            },
            timing = new
            {
                processTotalMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                connectionTotalMilliseconds = result?.TotalDuration.TotalMilliseconds ?? 0d,
                associationAndDomainValidationMilliseconds = result?.AssociationValidationDuration.TotalMilliseconds ?? 0d,
                initialReadMilliseconds = result?.InitialReadDuration.TotalMilliseconds ?? 0d
            },
            result = new
            {
                success = result?.IsSuccess == true,
                exitCode,
                message,
                warnings = result?.Warnings ?? Array.Empty<string>(),
                preparationErrors = result?.Preparation.Errors ?? Array.Empty<string>(),
                preparationWarnings = result?.Preparation.Warnings ?? Array.Empty<string>(),
                association = result?.Online is null ? null : new
                {
                    status = result.Online.Status.ToString(),
                    result.Online.AssociationSucceeded,
                    result.Online.DomainInventorySucceeded,
                    result.Online.SessionRemainsOpen,
                    result.Online.Message
                },
                domains = result?.Online?.Domains is null ? null : new
                {
                    expected = result.Online.Domains.ExpectedDomains,
                    observed = result.Online.Domains.ObservedDomains,
                    matched = result.Online.Domains.MatchedDomains,
                    missing = result.Online.Domains.MissingExpectedDomains,
                    extra = result.Online.Domains.ExtraObservedDomains,
                    result.Online.Domains.IsCompatible,
                    result.Online.Domains.IsExactMatch,
                    result.Online.Domains.Summary
                },
                initialRead = result?.InitialRead is null ? null : new
                {
                    status = result.InitialRead.Status.ToString(),
                    result.InitialRead.Message,
                    result.InitialRead.SuccessfulTargetCount,
                    result.InitialRead.FailedTargetCount,
                    result.InitialRead.ProjectedLeafCount,
                    maximumVariableReferencesPerRead = result.InitialRead.Plan.MaximumVariableReferencesPerRead,
                    maximumOutstandingReads = result.InitialRead.Plan.MaximumOutstandingReads,
                    targetCount = result.InitialRead.Plan.Targets.Count,
                    batchCount = result.InitialRead.Plan.Batches.Count,
                    batches = result.InitialRead.Batches.Select(batch => new
                    {
                        batch.BatchIndex,
                        targetCount = batch.Targets.Count,
                        references = batch.Targets.Select(target => target.MmsReference).ToArray(),
                        successCount = batch.Read.Results.Count(item => item.IsSuccess),
                        failureCount = batch.Read.Results.Count(item => !item.IsSuccess),
                        projectionErrorCount = batch.Projections.Sum(item => item.Errors.Count)
                    }).ToArray()
                },
                diagnostic
            }
        };

        await WriteEvidenceAsync(command.EvidencePath, evidence, cancellationToken).ConfigureAwait(false);
        return new SclSafeTrialRunResult(exitCode, result?.IsSuccess == true, message, command.EvidencePath);
    }

    private static async Task<SclSafeTrialRunResult> WriteInputFailureAsync(
        IReadOnlyList<string> args,
        string message,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"ARSAS-SCL-Trial-Invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        var evidence = new
        {
            schema = "arsas-scl-safe-trial-v2",
            capturedAtUtc = DateTimeOffset.UtcNow,
            success = false,
            exitCode = 30,
            message,
            arguments = args.ToArray()
        };
        await WriteEvidenceAsync(path, evidence, cancellationToken).ConfigureAwait(false);
        return new SclSafeTrialRunResult(30, false, message, path);
    }

    private static async Task WriteEvidenceAsync(
        string path,
        object evidence,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }
}
