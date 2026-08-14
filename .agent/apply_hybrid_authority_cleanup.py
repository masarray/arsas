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
    """        var plans = Iec61850ReportPlanner.BuildPlans(device, session.Points.Values);
        session.PendingReportPlans = plans;
        session.ReportSetupPending = plans.Count > 0 || session.Client.CanUseHybridReportPlanner(device);""",
    """        var hasHybridAuthority = session.Client.CanUseHybridReportPlanner(device);
        var plans = hasHybridAuthority
            ? Array.Empty<ReportControlPlan>()
            : Iec61850ReportPlanner.BuildPlans(device, session.Points.Values);
        session.PendingReportPlans = plans;
        session.ReportSetupPending = hasHybridAuthority || plans.Count > 0;""",
    "do-not-run-legacy-planner-under-engine-authority")

replace_once(
    """            $"Fast live start: points={session.Points.Count}, legacy compatibility plan(s)={plans.Count}, ARIEC hybrid authority={(session.Client.CanUseHybridReportPlanner(device) ? "available" : "unavailable")}, initial MMS scheduler={session.PollQueue.Count}, target={safePollMs} ms. Full signal discovery is not part of monitor start.");""",
    """            $"Fast live start: points={session.Points.Count}, legacy compatibility plan(s)={plans.Count}, ARIEC hybrid authority={(hasHybridAuthority ? "available" : "unavailable")}, initial MMS scheduler={session.PollQueue.Count}, target={safePollMs} ms. Full signal discovery is not part of monitor start.");""",
    "reuse-authority-decision")

replace_once(
    """                    var recoveryWillRun = !result.UsedDynamicDataSet &&
                                          plan.AllowDynamicDataSetWrites &&
                                          plan.Bindings.Count > 0;""",
    """                    var recoveryWillRun = !plan.IsEngineAuthoritative &&
                                          !result.UsedDynamicDataSet &&
                                          plan.AllowDynamicDataSetWrites &&
                                          plan.Bindings.Count > 0;""",
    "engine-plan-zero-coverage-log-must-not-promise-legacy-recovery")

path.write_text(text, encoding="utf-8")
print("Applied guarded hybrid authority cleanup.")
