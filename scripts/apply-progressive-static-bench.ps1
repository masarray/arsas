param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Replace-Exact {
    param(
        [string]$Text,
        [string]$Old,
        [string]$New,
        [string]$Label
    )

    if (-not $Text.Contains($Old, [System.StringComparison]::Ordinal)) {
        throw "Progressive Static bench patch anchor was not found: $Label"
    }

    return $Text.Replace($Old, $New, [System.StringComparison]::Ordinal)
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$selectionPath = Join-Path $ProjectRoot 'Services\Iec61850StaticDataSetAuthoritySelection.cs'
$runtimePath = Join-Path $ProjectRoot 'Services\Iec61850MonitorRuntime.cs'

$selection = [System.IO.File]::ReadAllText($selectionPath)
$runtime = [System.IO.File]::ReadAllText($runtimePath)

# Progressive bench policy: keep every exact static DataSet membership visible to the
# monitor. RCB-backed memberships still become report plans; uncovered MX measurements
# are handled later by the runtime's bounded MMS fallback.
$selection = Replace-Exact $selection @'
        var reportBackedDataSets = BuildReportBackedDataSetReferences(device);
        if (reportBackedDataSets.Count == 0)
            return new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);

'@ '' 'remove report-backed-only DataSet selection gate'

$selection = Replace-Exact $selection @'
                .Where(item => reportBackedDataSets.Contains(NormalizeLiteral(item.DataSetReference)))
'@ '' 'allow every exact static DataSet membership'

$selection = Replace-Exact $selection @'
            // A descriptor may carry more than one membership. Do not arbitrarily take the
            // first DataSet: choose only literal memberships that are backed by authoritative
            // report-control configuration.
'@ @'
            // A descriptor may carry more than one membership. Progressive Static keeps each
            // literal membership visible: configured RCBs remain primary, while uncovered MX
            // measurements may use bounded MMS polling. No fuzzy membership is introduced.
'@ 'update progressive selection comment'

# Static mode formerly disabled the poll queue globally. The bench policy schedules only
# uncovered MX/measurement points. Any point already bound to an active RCB remains excluded
# from the cyclic scheduler, preserving event-driven Digital/status semantics.
$runtime = Replace-Exact $runtime @'
        session.PollQueue.Clear();
        if (session.StaticDataSetReportOnly)
        {
            foreach (var state in session.States.Values)
                state.NextPollUtc = DateTime.MaxValue;
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var index = 0;
'@ @'
        session.PollQueue.Clear();
        if (session.StaticDataSetReportOnly)
        {
            var staticNowUtc = DateTime.UtcNow;
            var staticIndex = 0;
            foreach (var point in session.Points.Values)
            {
                var state = session.States[point.PointKey];
                var reportAssigned = session.PointPlanIds.ContainsKey(point.PointKey);
                var measurementFallback = IsAnalogPoint(point) ||
                    point.FunctionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase);

                if (reportAssigned || !measurementFallback)
                {
                    state.NextPollUtc = DateTime.MaxValue;
                    continue;
                }

                var dueUtc = staggerForRecovery
                    ? staticNowUtc.AddMilliseconds(SmartReconnectPolicy.GetRecoveryStaggerDelayMs(staticIndex++))
                    : staticNowUtc;
                state.NextPollUtc = dueUtc;
                state.AcquisitionLabel = "Static DataSet: MMS polling fallback";
                state.SourceMode = state.AcquisitionLabel;
                state.Reason = "no active configured RCB coverage for MX measurement";
                state.Status = "Queued / progressive MMS fallback";
                if (staggerForRecovery)
                    state.NextCompanionPollUtc = session.RecoveryWarmupUntilUtc;
                session.PollQueue.Enqueue(point.PointKey, dueUtc.Ticks);
            }
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var index = 0;
'@ 'schedule uncovered static MX points for MMS polling'

# Make the live-grid source explicit when an uncovered static measurement is read by MMS.
$runtime = Replace-Exact $runtime @'
                var sourceMode = "MMS polling";
                var reason = "cyclic";
                var status = "Live / polling";

                if (reportAssigned)
'@ @'
                var sourceMode = "MMS polling";
                var reason = "cyclic";
                var status = "Live / polling";

                if (session.StaticDataSetReportOnly && !reportAssigned)
                {
                    sourceMode = "Static DataSet: MMS polling fallback";
                    reason = "uncovered MX measurement / bounded cyclic MMS";
                    status = "Live / progressive MMS fallback";
                }

                if (reportAssigned)
'@ 'label static MX fallback reads'

# Replace the strict-static summary with a truthful split between report-covered,
# MX polling fallback, and unresolved discrete points.
$runtime = Replace-Exact $runtime @'
        if (session.StaticDataSetReportOnly)
        {
            session.Device.AcquisitionMode = staticReportCount > 0
                ? $"Static DataSet reporting • RCB {staticReportCount} • unresolved {unassignedCount}"
                : $"Static DataSet reporting unavailable • unresolved {unassignedCount}";
            session.Device.Detail = staticReportCount > 0
                ? $"{session.Points.Count} DataSet-derived point(s): configured RCB reporting is the process-value authority; {unassignedCount} point(s) are unresolved/unavailable. Cyclic MMS process polling is disabled."
                : $"{session.Points.Count} DataSet-derived point(s): no configured RCB could be armed. Values remain unavailable; MMS process fallback is disabled by Static DataSet mode.";
            session.Device.RefreshComputed();
            Log("INFO", session.Device.Name,
                $"Static DataSet acquisition ready: static report plan(s)={staticReportCount}, report-covered={session.PointPlanIds.Count}, unresolved={unassignedCount}, cyclic MMS process polling=0.");
            return;
        }
'@ @'
        if (session.StaticDataSetReportOnly)
        {
            var measurementFallbackCount = session.Points.Values.Count(point =>
                !session.PointPlanIds.ContainsKey(point.PointKey) &&
                (IsAnalogPoint(point) || point.FunctionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase)));
            var unresolvedDiscreteCount = Math.Max(0, unassignedCount - measurementFallbackCount);

            session.Device.AcquisitionMode = staticReportCount > 0
                ? $"Progressive Static • RCB {staticReportCount} • MX fallback {measurementFallbackCount} • unresolved {unresolvedDiscreteCount}"
                : $"Progressive Static • MX fallback {measurementFallbackCount} • unresolved {unresolvedDiscreteCount}";
            session.Device.Detail =
                $"{session.Points.Count} static DataSet-derived point(s): configured RCB reporting stays primary; " +
                $"{measurementFallbackCount} uncovered MX/measurement point(s) use bounded MMS polling; " +
                $"{unresolvedDiscreteCount} uncovered discrete point(s) remain fail-closed. Dynamic DataSet writes remain disabled.";
            session.Device.RefreshComputed();
            Log("INFO", session.Device.Name,
                $"Progressive Static acquisition ready: static report plan(s)={staticReportCount}, report-covered={session.PointPlanIds.Count}, MX MMS fallback={measurementFallbackCount}, unresolved-discrete={unresolvedDiscreteCount}, dynamic DataSet writes=0.");
            return;
        }
'@ 'report progressive static acquisition summary'

$runtime = $runtime.Replace(
    'Static DataSet report-only start:',
    'Progressive Static DataSet start:',
    [System.StringComparison]::Ordinal)
$runtime = $runtime.Replace(
    'Static DataSet report-only mode: arming configured RCBs immediately; no cyclic MMS initial-image scheduler is active.',
    'Progressive Static mode: arming configured RCBs first; uncovered MX measurements will enter bounded MMS polling only after report planning.',
    [System.StringComparison]::Ordinal)
$runtime = $runtime.Replace(
    'Engine fallback candidates are diagnostic only and are not scheduled as MMS process polling.',
    'Configured-RCB planning remains authoritative; runtime schedules MMS only for uncovered MX/measurement points.',
    [System.StringComparison]::Ordinal)

[System.IO.File]::WriteAllText($selectionPath, $selection, $utf8)
[System.IO.File]::WriteAllText($runtimePath, $runtime, $utf8)

Write-Host 'Progressive Static bench patch applied.'
Write-Host 'Policy: configured RCB first; uncovered MX/measurement -> bounded MMS polling; uncovered discrete -> fail-closed; dynamic DataSet writes -> disabled.'
