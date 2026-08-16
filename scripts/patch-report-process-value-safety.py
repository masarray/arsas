from pathlib import Path

runtime_path = Path("Services/Iec61850MonitorRuntime.cs")
lock_test_path = Path("tests/ARSAS.Tests/OfflineDataSetSignalSelectionRegressionTests.cs")

runtime = runtime_path.read_text(encoding="utf-8")
lock_test = lock_test_path.read_text(encoding="utf-8")

old_state = '''        public bool StaleReportSuppressedLogged { get; set; }
        public int ConsecutiveErrors { get; set; }'''
new_state = '''        public bool StaleReportSuppressedLogged { get; set; }
        public bool ReportValueRejectedLogged { get; set; }
        public int ConsecutiveErrors { get; set; }'''
if runtime.count(old_state) != 1:
    raise SystemExit(f"Runtime state anchor count={runtime.count(old_state)}")
runtime = runtime.replace(old_state, new_state, 1)

old_ingest = '''                var state = session.States[point.PointKey];
                var display = update.HasValue
                    ? Iec61850ValueFormatter.Format(update.Value, point.IecDataType, point.Unit)
                    : state.Value;
                if (update.HasValue && LooksLikeReferenceEcho(display, update.Reference, point.IecReference))
                    continue;

                var receivedUtc = update.UpdatedAt == default ? DateTime.UtcNow : update.UpdatedAt.UtcDateTime;'''
new_ingest = '''                var state = session.States[point.PointKey];
                var display = update.HasValue
                    ? Iec61850ValueFormatter.Format(update.Value, point.IecDataType, point.Unit)
                    : state.Value;
                if (update.HasValue && LooksLikeReferenceEcho(display, update.Reference, point.IecReference))
                    continue;

                if (update.HasValue && !ReportProcessValueSafety.IsSafe(
                        update.Value,
                        display,
                        point.IecDataType,
                        point.IecReference,
                        out var rejectionReason))
                {
                    // A malformed/misaligned report is still useful as proof that the
                    // RCB is alive, but its process value is not authoritative. Do not
                    // mutate state or SOE history; keep MMS verification/fallback active.
                    state.ReportTrafficSeen = true;
                    state.LastReportUtc = DateTime.UtcNow;
                    state.ReportChangeVerified = false;
                    state.ReportMissLogged = false;
                    if (!state.ReportValueRejectedLogged)
                    {
                        state.ReportValueRejectedLogged = true;
                        var rawSummary = update.Value ?? "-";
                        if (rawSummary.Length > 120)
                            rawSummary = rawSummary[..120] + "…";
                        Log("WARN", session.Device.Name,
                            $"REPORT_VALUE_REJECTED: {point.SignalName} ({point.IecReference}) rejected report value '{rawSummary}'. {rejectionReason} MMS verification/fallback remains authoritative.");
                    }
                    continue;
                }
                if (update.HasValue)
                    state.ReportValueRejectedLogged = false;

                var receivedUtc = update.UpdatedAt == default ? DateTime.UtcNow : update.UpdatedAt.UtcDateTime;'''
if runtime.count(old_ingest) != 1:
    raise SystemExit(f"Report ingestion anchor count={runtime.count(old_ingest)}")
runtime = runtime.replace(old_ingest, new_ingest, 1)

old_sha = 'Assert.Contains("e23b295b87760be8f7f0ce978a6987027ea50523", source, StringComparison.OrdinalIgnoreCase);'
new_sha = 'Assert.Contains("becda399b4a3ae34831215fc915798b4f846c1be", source, StringComparison.OrdinalIgnoreCase);'
old_pr = 'Assert.Contains("\\\"sourcePullRequest\\\": 80", source, StringComparison.Ordinal);'
new_pr = 'Assert.Contains("\\\"sourcePullRequest\\\": 81", source, StringComparison.Ordinal);'
for old, new in ((old_sha, new_sha), (old_pr, new_pr)):
    if lock_test.count(old) != 1:
        raise SystemExit(f"Lock test anchor count={lock_test.count(old)}: {old}")
    lock_test = lock_test.replace(old, new, 1)

anchor = '        Assert.Contains("DataRef-enabled InformationReport ordering", source, StringComparison.OrdinalIgnoreCase);\n'
addition = anchor + '        Assert.Contains("zero OptFlds", source, StringComparison.OrdinalIgnoreCase);\n        Assert.Contains("quarantining unmapped canonical report metadata", source, StringComparison.OrdinalIgnoreCase);\n'
if lock_test.count(anchor) != 1:
    raise SystemExit(f"Lock purpose anchor count={lock_test.count(anchor)}")
lock_test = lock_test.replace(anchor, addition, 1)

runtime_path.write_text(runtime, encoding="utf-8", newline="\n")
lock_test_path.write_text(lock_test, encoding="utf-8", newline="\n")
print("Integrated report process-value safety before runtime state/SOE mutation and updated engine pin regression.")
