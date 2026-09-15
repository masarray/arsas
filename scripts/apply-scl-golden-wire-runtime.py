from pathlib import Path

runtime_path = Path("Services/Iec61850MonitorRuntime.cs")
text = runtime_path.read_text(encoding="utf-8")

start = text.index("    public async Task ConnectUsingCachedModelAsync(")
end = text.index("    public async Task<IReadOnlyList<Iec61850MonitorPoint>> StartMonitoringAsync(", start)
replacement = r'''    public async Task ConnectUsingCachedModelAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken,
        IProgress<IedDiscoveryProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ValidateEndpoint(device);

        var connectionPath = Iec61850ConnectionPathPolicy.SelectForFastConnect(device);
        if (connectionPath == Iec61850ConnectionPath.FullDiscovery)
            throw new InvalidOperationException($"{device.Name} has no trusted SCL design or successful saved discovery model. Run a full discovery first.");
        if (connectionPath == Iec61850ConnectionPath.CachedLiveModel &&
            (!device.HasDiscoveryCache || device.Signals.Count == 0))
            throw new InvalidOperationException($"{device.Name} has no successful saved discovery model. Run a full discovery first.");

        progress?.Report(new IedDiscoveryProgress(
            IedDiscoveryStage.PreparingSession,
            connectionPath == Iec61850ConnectionPath.SclAssisted
                ? "Verifying trusted SCL/CID source…"
                : "Preparing saved IEC 61850 model…",
            4d,
            1,
            4));

        await StopDeviceAsync(device.DeviceId).ConfigureAwait(false);
        var session = new DeviceSession
        {
            Device = device,
            Client = new NativeIec61850Client()
        };
        _sessions[device.DeviceId] = session;

        device.IsConnected = false;
        device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot(
            connectionPath == Iec61850ConnectionPath.SclAssisted
                ? "Preparing verified SCL-assisted connection"
                : "Preparing fast connection from saved model");
        device.Status = connectionPath == Iec61850ConnectionPath.SclAssisted
            ? "SCL connecting"
            : "Fast connecting";
        device.Detail = connectionPath == Iec61850ConnectionPath.SclAssisted
            ? $"Opening {device.IpAddress}:{device.Port} with verified SCL association identity."
            : $"Opening {device.IpAddress}:{device.Port} with the saved discovery model.";
        Log("INFO", device.Name,
            connectionPath == Iec61850ConnectionPath.SclAssisted
                ? "Trusted SCL authority selected for Play; source SHA will be verified before any socket is opened and no discovery fallback is allowed."
                : $"Fast reconnect using saved model ({device.SignalCount:N0} signals); full live discovery is skipped.");

        try
        {
            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.OpeningTcp,
                $"Opening TCP {device.IpAddress}:{device.Port}…",
                24d,
                2,
                4));

            connectionPath = await ConnectUsingSelectedFastPathAsync(
                session,
                cancellationToken,
                allowCachedRetry: true).ConfigureAwait(false);
            if (!session.Client.IsConnected)
            {
                device.Status = "Connection failed";
                device.Detail = string.IsNullOrWhiteSpace(session.Client.LastErrorMessage)
                    ? "The IED did not complete IEC 61850 ACSE/MMS association."
                    : session.Client.LastErrorMessage;
                throw new InvalidOperationException(device.Detail);
            }

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.AssociatingMms,
                connectionPath == Iec61850ConnectionPath.SclAssisted
                    ? "SCL association validated. Restoring SCL signal workspace…"
                    : "ACSE/MMS associated. Restoring saved signal workspace…",
                74d,
                3,
                4));

            device.IsConnected = true;
            device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot(
                connectionPath == Iec61850ConnectionPath.SclAssisted
                    ? "Verified SCL-assisted connection complete"
                    : "Fast connection complete");
            device.Status = "Ready";
            device.Detail = connectionPath == Iec61850ConnectionPath.SclAssisted
                ? $"Connected from verified SCL: {device.SignalCount:N0} signal(s), {device.SelectedSignalCount:N0} selected. Domain/VMD validation and bounded FC-root reads completed; full discovery was skipped."
                : $"Connected with saved model: {device.SignalCount:N0} signal(s), {device.SelectedSignalCount:N0} selected. Full discovery was skipped.";
            device.AcquisitionMode = connectionPath == Iec61850ConnectionPath.SclAssisted
                ? "Verified SCL • ready to monitor"
                : "Saved model • ready to monitor";
            device.RefreshComputed();

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.Complete,
                connectionPath == Iec61850ConnectionPath.SclAssisted
                    ? "Verified SCL online path ready for static reporting."
                    : "Saved model restored — ready for live values.",
                100d,
                4,
                4));

            Log("INFO", device.Name,
                connectionPath == Iec61850ConnectionPath.SclAssisted
                    ? "Trusted SCL connection complete. Static reporting will reuse SCL RCB/DataSet authority; no network DataSet-directory browse, dynamic DataSet mutation, or implicit GI is permitted."
                    : "Fast reconnect complete. Reporting setup will validate only the acquisition objects required by the selected points; the full signal scan remains cached.");
        }
        catch (Exception ex)
        {
            device.LastDiagnosticSnapshot = session.Client.CaptureDiagnosticSnapshot(
                connectionPath == Iec61850ConnectionPath.SclAssisted
                    ? "Verified SCL-assisted connection failed"
                    : "Fast TCP/ACSE/MMS connection failed",
                ex);
            if (!session.Client.IsConnected)
            {
                device.IsConnected = false;
                _sessions.TryRemove(device.DeviceId, out _);
                await DisposeClientForReconnectAsync(
                    session.Client,
                    device.Name,
                    SmartReconnectPolicy.ClientCleanupBudget,
                    CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            device.RefreshComputed();
        }
    }

    private async Task<Iec61850ConnectionPath> ConnectUsingSelectedFastPathAsync(
        DeviceSession session,
        CancellationToken cancellationToken,
        bool allowCachedRetry)
    {
        var device = session.Device;
        var path = Iec61850ConnectionPathPolicy.SelectForFastConnect(device);
        if (path == Iec61850ConnectionPath.FullDiscovery)
            throw new InvalidOperationException($"{device.Name} requires full discovery; fast-connect cannot invent a model authority.");

        if (path == Iec61850ConnectionPath.SclAssisted)
        {
            var verified = await VerifiedSclSourceLoader.LoadAsync(
                device.SclSourcePath,
                device.SclSourceSha256,
                cancellationToken).ConfigureAwait(false);
            var result = await session.Client.ConnectUsingSclAsync(
                verified.Xml,
                device.SclIedName,
                device.SclAccessPointName,
                device.IpAddress,
                device.Port,
                cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess || !session.Client.IsConnected)
                throw new InvalidOperationException(result.Message);

            device.LiveDiscoveryModel = session.Client.LastLiveModel ?? device.SclWorkspace?.DesignModel;
            Log("INFO", device.Name,
                $"Verified SCL authority active: SHA256={verified.Sha256}; IED={device.SclIedName}; AP={device.SclAccessPointName}; maxReadRefs={result.Preparation.InitialReadPlan?.MaximumVariableReferencesPerRead ?? 0}.");
            return path;
        }

        if (allowCachedRetry)
            await ConnectCachedAssociationWithRetryAsync(session, cancellationToken).ConfigureAwait(false);
        else
            await session.Client.ConnectAsync(device.IpAddress, device.Port, cancellationToken).ConfigureAwait(false);
        return path;
    }

'''
text = text[:start] + replacement + text[end:]

old_start = '''                var result = plan.IsEngineAuthoritative
                    ? await session.Client.StartHybridReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false)
                    : await session.Client.StartReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false);'''
new_start = '''                var result = session.StaticDataSetReportOnly && session.Client.HasTrustedSclOnlineAuthority
                    ? await session.Client.StartTrustedSclStaticReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false)
                    : plan.IsEngineAuthoritative
                        ? await session.Client.StartHybridReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false)
                        : await session.Client.StartReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false);'''
if old_start not in text:
    raise SystemExit("StartReportPlansAsync seam not found")
text = text.replace(old_start, new_start, 1)

marker = '''    private async Task<IReadOnlyList<ReportControlPlan>> BuildReportPlansForCurrentAssociationAsync(
        DeviceSession session,
        IReadOnlyList<ReportControlPlan> legacyPlans,
        CancellationToken cancellationToken)
    {
'''
inject = marker + '''        if (session.StaticDataSetReportOnly && session.Client.HasTrustedSclOnlineAuthority)
        {
            session.HybridValidation.Reset(null);
            var trustedPlans = legacyPlans.Count > 0
                ? legacyPlans
                : Iec61850ReportPlanner.BuildPlans(
                    session.Device,
                    session.Points.Values,
                    allowDynamicDataSetWrites: false);
            Log("INFO", session.Device.Name,
                $"Trusted SCL report planning retained {trustedPlans.Count} local static candidate(s). DataSet membership and RCB identity remain SCL-authoritative; online directory discovery and Hybrid availability probing are bypassed.");
            return trustedPlans.Where(plan => !plan.AllowDynamicDataSetWrites).ToArray();
        }

'''
if marker not in text:
    raise SystemExit("BuildReportPlansForCurrentAssociationAsync seam not found")
text = text.replace(marker, inject, 1)

old_reconnect = '''        try
        {
            await replacement.ConnectAsync(
                session.Device.IpAddress,
                session.Device.Port,
                connectTimeout.Token).ConfigureAwait(false);
        }
'''
new_reconnect = '''        try
        {
            var reconnectPath = await ConnectUsingSelectedFastPathAsync(
                session,
                connectTimeout.Token,
                allowCachedRetry: false).ConfigureAwait(false);
            Log("INFO", session.Device.Name,
                reconnectPath == Iec61850ConnectionPath.SclAssisted
                    ? "Smart reconnect reused verified SCL authority; no cached-association or discovery fallback was attempted."
                    : "Smart reconnect reused the saved live-model association path.");
        }
'''
if old_reconnect not in text:
    raise SystemExit("TryReconnectAsync connect seam not found")
text = text.replace(old_reconnect, new_reconnect, 1)

runtime_path.write_text(text, encoding="utf-8")

regression = r'''using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclGoldenWireIntegrationContractTests
{
    [Fact]
    public void Runtime_FastPlay_PrefersVerifiedSclWithoutDiscoveryFallback()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        var start = source.IndexOf("public async Task ConnectUsingCachedModelAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<IReadOnlyList<Iec61850MonitorPoint>> StartMonitoringAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var connect = source[start..end];

        Assert.Contains("Iec61850ConnectionPathPolicy.SelectForFastConnect", connect, StringComparison.Ordinal);
        Assert.Contains("VerifiedSclSourceLoader.LoadAsync", connect, StringComparison.Ordinal);
        Assert.Contains("ConnectUsingSclAsync", connect, StringComparison.Ordinal);
        Assert.Contains("allowCachedRetry: true", connect, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAndDiscoverAsync", connect, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscoverSignalsAsync", connect, StringComparison.Ordinal);
        Assert.True(
            connect.IndexOf("VerifiedSclSourceLoader.LoadAsync", StringComparison.Ordinal) <
            connect.IndexOf("ConnectUsingSclAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Runtime_TrustedSclStaticReporting_BypassesHybridAndLegacyStartPaths()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        Assert.Contains("session.StaticDataSetReportOnly && session.Client.HasTrustedSclOnlineAuthority", source, StringComparison.Ordinal);
        Assert.Contains("StartTrustedSclStaticReportMonitorAsync", source, StringComparison.Ordinal);
        Assert.Contains("DataSet membership and RCB identity remain SCL-authoritative", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TrustedSclStaticAdapter_HasGoldenWireSafetyContract()
    {
        var source = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.TrustedSclStaticReporting.cs"));
        Assert.Contains("TryGetTrustedSclDataSetDirectory", source, StringComparison.Ordinal);
        Assert.Contains("StartStaticSclReportMonitorAsync", source, StringComparison.Ordinal);
        Assert.Contains("triggerGeneralInterrogation: false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDataSetDirectoriesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_Reconnect_ReusesSelectedAuthorityInsteadOfSilentlyDowngrading()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        var start = source.IndexOf("private async Task TryReconnectAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private void ScheduleReconnectRetry", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var reconnect = source[start..end];

        Assert.Contains("ConnectUsingSelectedFastPathAsync", reconnect, StringComparison.Ordinal);
        Assert.Contains("no cached-association or discovery fallback was attempted", reconnect, StringComparison.Ordinal);
        Assert.DoesNotContain("replacement.ConnectAsync", reconnect, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
'''
Path("tests/ARSAS.Tests/SclGoldenWireIntegrationContractTests.cs").write_text(regression, encoding="utf-8")
