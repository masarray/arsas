from pathlib import Path

path = Path("Services/Iec61850MonitorRuntime.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one source match, found {count}")
    text = text.replace(old, new, 1)


replace_once(
    "        public int ControlCommandActive;\n    }",
    "        public int ControlCommandActive;\n        public HybridReportPhysicalValidationTracker HybridValidation { get; } = new();\n    }",
    "device-session-validation-tracker")

replace_once(
    "        session.HealthProbePointKey = string.Empty;\n\n        var safePollMs",
    "        session.HealthProbePointKey = string.Empty;\n        session.HybridValidation.Reset(null);\n\n        var safePollMs",
    "reset-validation-tracker")

replace_once(
    "        session.PendingReportPlans = plans;\n        session.ReportSetupPending = plans.Count > 0;",
    "        session.PendingReportPlans = plans;\n        session.ReportSetupPending = plans.Count > 0 || session.Client.CanUseHybridReportPlanner(device);",
    "arm-hybrid-planner")

replace_once(
    "        device.AcquisitionMode = plans.Count > 0\n            ? \"MMS live start • arming smart reporting\"",
    "        device.AcquisitionMode = session.ReportSetupPending\n            ? \"MMS live start • arming ARIEC hybrid reporting\"",
    "hybrid-start-mode")

replace_once(
    "        device.Detail = plans.Count > 0\n            ? $\"{session.Points.Count} point(s): MMS is reading the initial live image immediately while static/dynamic reporting is validated in the same independent IED session.\"",
    "        device.Detail = session.ReportSetupPending\n            ? $\"{session.Points.Count} point(s): MMS is reading the initial live image immediately while the ARIEC hybrid planner validates fresh static/dynamic BRCB/URCB capability in the same independent IED session.\"",
    "hybrid-start-detail")

replace_once(
    "            $\"Fast live start: points={session.Points.Count}, pending report plan(s)={plans.Count}, initial MMS scheduler={session.PollQueue.Count}, target={safePollMs} ms. Full signal discovery is not part of monitor start.\");",
    "            $\"Fast live start: points={session.Points.Count}, legacy compatibility plan(s)={plans.Count}, ARIEC hybrid authority={(session.Client.CanUseHybridReportPlanner(device) ? \"available\" : \"unavailable\")}, initial MMS scheduler={session.PollQueue.Count}, target={safePollMs} ms. Full signal discovery is not part of monitor start.\");",
    "hybrid-start-log")

old_setup = """        await StartReportPlansAsync(session, plans, cancellationToken).ConfigureAwait(false);
        ResetPollQueue(session);
        UpdateDeviceAcquisitionSummary(session);"""
new_setup = """        if (session.Client.CanUseHybridReportPlanner(session.Device))
        {
            NativeHybridReportPlanningResult hybrid;
            try
            {
                hybrid = await session.Client.BuildHybridReportPlansAsync(
                    session.Device,
                    session.Points.Values.ToArray(),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                hybrid = new NativeHybridReportPlanningResult
                {
                    IsAuthoritative = true,
                    Authority = "ARIEC61850 hybrid acquisition",
                    Status = "Planner failure / polling safe",
                    Summary = $"ARIEC hybrid planning failed closed: {ex.GetType().Name}: {ex.Message}. No local RCB heuristic was substituted; bounded MMS polling remains active.",
                    RequestedPointCount = session.Points.Count,
                    PollingPointKeys = session.Points.Keys.ToArray(),
                    PollingFallbackSignalCount = session.Points.Count,
                    Warnings = [$"Hybrid planning exception: {ex.GetType().Name}: {ex.Message}"]
                };
            }

            session.HybridValidation.Reset(hybrid);
            plans = hybrid.ReportPlans;
            Log("INFO", session.Device.Name,
                $"Hybrid authority={hybrid.Authority}; status={hybrid.Status}; requested={hybrid.RequestedPointCount}, catalog={hybrid.CatalogMappedPointCount}, staticBRCB={hybrid.StaticBrcbSignalCount}, staticURCB={hybrid.StaticUrcbSignalCount}, dynamicBRCB={hybrid.DynamicBrcbSignalCount}, dynamicURCB={hybrid.DynamicUrcbSignalCount}, polling={hybrid.PollingFallbackSignalCount}, uncovered={hybrid.UncoveredSignalCount}. {hybrid.Summary}");
            foreach (var warning in hybrid.Warnings.Take(5))
                Log("WARN", session.Device.Name, warning);
        }
        else
        {
            session.HybridValidation.Reset(null);
            Log("INFO", session.Device.Name,
                "ARIEC typed live-model authority is unavailable for this saved/session model; retaining the existing legacy report planner only as compatibility fallback.");
        }

        await StartReportPlansAsync(session, plans, cancellationToken).ConfigureAwait(false);
        ResetPollQueue(session);
        UpdateDeviceAcquisitionSummary(session);"""
replace_once(old_setup, new_setup, "hybrid-plan-consumption")

replace_once(
    "                var result = await session.Client.StartReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false);\n                if (!result.IsSuccess)",
    "                var result = plan.IsEngineAuthoritative\n                    ? await session.Client.StartHybridReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false)\n                    : await session.Client.StartReportMonitorAsync(plan, cancellationToken).ConfigureAwait(false);\n                session.HybridValidation.RecordActivation(plan, result);\n                if (!result.IsSuccess)",
    "execute-authoritative-plan")

replace_once(
    "                plan.Status = result.UsedDynamicDataSet ? \"Dynamic active\" : \"Static active\";",
    "                plan.Status = plan.IsEngineAuthoritative\n                    ? $\"{plan.EngineAcquisitionKind} active\"\n                    : result.UsedDynamicDataSet ? \"Dynamic active\" : \"Static active\";",
    "preserve-engine-kind")

replace_once(
    "                if (!result.UsedDynamicDataSet &&\n                    plan.AllowDynamicDataSetWrites &&",
    "                if (!plan.IsEngineAuthoritative &&\n                    !result.UsedDynamicDataSet &&\n                    plan.AllowDynamicDataSetWrites &&",
    "disable-legacy-recovery-for-engine-plan")

old_warning_loop = """            foreach (var warning in slice.Warnings.Take(2))
                Log("WARN", session.Device.Name, warning);"""
new_warning_loop = """            var verifiedReportPointKeys = slice.Updates
                .Select(update => FindPointForReportReference(session, update.Reference))
                .Where(point => point is not null)
                .Select(point => point!)
                .Where(point => session.States.TryGetValue(point.PointKey, out var state) && state.ReportChangeVerified)
                .Select(point => point.PointKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            session.HybridValidation.RecordSlice(plan, slice, verifiedReportPointKeys);

            foreach (var warning in slice.Warnings.Take(2))
                Log("WARN", session.Device.Name, warning);"""
replace_once(old_warning_loop, new_warning_loop, "record-physical-report-evidence")

capture_anchor = """    public async Task StopMonitoringAsync(string deviceId)
    {"""
capture_method = """    public HybridReportPhysicalValidationSnapshot CaptureHybridReportPhysicalValidation(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!_sessions.TryGetValue(deviceId, out var session))
            throw new InvalidOperationException($"No IEC 61850 runtime session exists for device '{deviceId}'.");
        return session.HybridValidation.Capture(session.Device);
    }

    public async Task StopMonitoringAsync(string deviceId)
    {"""
replace_once(capture_anchor, capture_method, "physical-validation-snapshot-api")

path.write_text(text, encoding="utf-8")
print("Applied guarded ARIEC hybrid acquisition runtime integration.")
