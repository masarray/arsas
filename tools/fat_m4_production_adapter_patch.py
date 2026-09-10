from pathlib import Path


def replace_exact(path: Path, old: str, new: str, expected: int = 1) -> None:
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != expected:
        raise SystemExit(f"{path}: expected {expected} occurrence(s), found {count}: {old[:100]!r}")
    path.write_text(text.replace(old, new), encoding="utf-8")


coordinator = Path("Services/IoTesting/IoTestMultiSessionCoordinator.cs")
window = Path("IoListTestingWindow.xaml.cs")

replace_exact(
    coordinator,
    '''    public IoTestSessionActionResult Pause(string reason = "Paused by operator")
        => SelectedAction(
            controller => controller.Pause(reason),
            "No FAT evidence session is active for the selected IED.");

    public IoTestSessionActionResult Resume()
        => SelectedAction(
            controller => controller.Resume(),
            "No paused or interrupted FAT evidence session belongs to the selected IED.");

    public IoTestSessionActionResult Stop(string reason = "Stopped by operator")
        => SelectedAction(
            controller => controller.Stop(reason),
            "No FAT evidence session is active for the selected IED.");
''',
    '''    public IoTestSessionActionResult Pause(string reason = "Paused by operator")
        => SelectedAction(
            controller => controller.Pause(reason),
            "No FAT evidence session is active for the selected IED.");

    public IoTestSessionActionResult Pause(IoTestIedPlan? ied, string reason = "Paused by operator")
        => TargetAction(
            ied,
            controller => controller.Pause(reason),
            "No FAT evidence session is active for the latched IED.");

    public IoTestSessionActionResult Resume()
        => SelectedAction(
            controller => controller.Resume(),
            "No paused or interrupted FAT evidence session belongs to the selected IED.");

    public IoTestSessionActionResult Resume(IoTestIedPlan? ied)
        => TargetAction(
            ied,
            controller => controller.Resume(),
            "No paused or interrupted FAT evidence session belongs to the latched IED.");

    public IoTestSessionActionResult Stop(string reason = "Stopped by operator")
        => SelectedAction(
            controller => controller.Stop(reason),
            "No FAT evidence session is active for the selected IED.");

    public IoTestSessionActionResult Stop(IoTestIedPlan? ied, string reason = "Stopped by operator")
        => TargetAction(
            ied,
            controller => controller.Stop(reason),
            "No FAT evidence session is active for the latched IED.");
''')

replace_exact(
    coordinator,
    '''    private IoTestSessionActionResult SelectedAction(
        Func<IoTestSessionController, IoTestSessionActionResult> action,
        string missingMessage)
''',
    '''    private IoTestSessionActionResult TargetAction(
        IoTestIedPlan? ied,
        Func<IoTestSessionController, IoTestSessionActionResult> action,
        string missingMessage)
    {
        ThrowIfDisposed();
        if (ied == null)
            return IoTestSessionActionResult.Failure(missingMessage);

        IoTestSessionController? controller;
        lock (_sync)
            _controllers.TryGetValue(ied, out controller);
        if (controller == null)
            return IoTestSessionActionResult.Failure(missingMessage);

        var result = action(controller);
        RaiseProjectionProperties();
        return result;
    }

    private IoTestSessionActionResult SelectedAction(
        Func<IoTestSessionController, IoTestSessionActionResult> action,
        string missingMessage)
''')

replace_exact(
    window,
    '''        // Capture target latch: keep this local IED for the complete async prepare +
        // Start transaction even if the global Engineering Explorer changes selection.
        var selectedIed = SelectedIed;
        if (selectedIed?.IsPreparing == true)
            return;

        var preflight = IoTestSessionPreflight.Validate(selectedIed);
        if (!preflight.Succeeded)
        {
            ShowActionResult(preflight, "FAT session scope is not ready");
            return;
        }

        PreparationStatusText = $"Connecting {selectedIed!.IedName} · {selectedIed.IpAddress}:102";
''',
    '''        // M4 freezes both the IED owner and exact evidence scope before any asynchronous
        // Engineering preparation. Explorer navigation may continue, but it cannot redirect
        // this transaction or silently widen the production capture scope.
        var requestedIed = SelectedIed;
        if (requestedIed?.IsPreparing == true)
            return;

        IoFatCaptureTargetLease captureLease;
        try
        {
            captureLease = IoFatProductionControllerAdapter.LatchStartTarget(Project, requestedIed);
        }
        catch (InvalidOperationException ex)
        {
            ShowActionResult(
                IoTestSessionActionResult.Failure(ex.Message),
                "FAT session scope is not ready");
            return;
        }

        var selectedIed = captureLease.Ied;
        PreparationStatusText = $"Connecting {selectedIed.IedName} · {selectedIed.IpAddress}:102";
''')

replace_exact(
    window,
    '''            var result = Session.Start(selectedIed);
''',
    '''            // Revalidate the frozen identity/configuration only after Engineering has
            // completed connection preparation. The adapter delegates to the unchanged
            // production session controller, which remains the sole evidence writer.
            var result = IoFatProductionControllerAdapter.StartLatched(Project, Session, captureLease);
''')

replace_exact(
    window,
    '''    private void PauseSession_Click(object sender, RoutedEventArgs e)
    {
        var result = Session.Pause();
        ShowActionResult(result, "FAT session could not pause");
        if (result.Succeeded)
            Storage?.SaveNow();
    }

    private void ResumeSession_Click(object sender, RoutedEventArgs e)
    {
        var result = Session.Resume();
        ShowActionResult(result, "FAT session could not resume");
        if (result.Succeeded)
            Storage?.ScheduleSave();
    }

    private void StopSession_Click(object sender, RoutedEventArgs e)
    {
        var result = Session.Stop();
        ShowActionResult(result, "FAT session could not stop");
        if (result.Succeeded)
            Storage?.SaveNow();
    }
''',
    '''    private void PauseSession_Click(object sender, RoutedEventArgs e)
    {
        var targetIed = SelectedIed;
        var result = Session.Pause(targetIed);
        ShowActionResult(result, "FAT session could not pause");
        if (result.Succeeded)
            Storage?.SaveNow();
    }

    private void ResumeSession_Click(object sender, RoutedEventArgs e)
    {
        // Continue is explicitly IED-targeted. A later Explorer change cannot make the
        // production controller rebind this active evidence session to another device.
        var targetIed = SelectedIed;
        var result = Session.Resume(targetIed);
        ShowActionResult(result, "FAT session could not resume");
        if (result.Succeeded)
            Storage?.ScheduleSave();
    }

    private void StopSession_Click(object sender, RoutedEventArgs e)
    {
        var targetIed = SelectedIed;
        var result = Session.Stop(targetIed);
        ShowActionResult(result, "FAT session could not stop");
        if (result.Succeeded)
            Storage?.SaveNow();
    }
''')

print("M4 production adapter wiring applied")
