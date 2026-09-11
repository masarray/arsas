using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// M4 transaction boundary between Engineering's mutable view context and the existing
/// production FAT evidence engine. It freezes the operator-selected IED and exact capture
/// scope before asynchronous connection preparation, then revalidates that lease before
/// delegating to <see cref="IoTestMultiSessionCoordinator.Start(IoTestIedPlan?, IReadOnlyCollection{IoTestPointPlan}?)"/>.
///
/// This class deliberately does not parse SCL, discover/connect an IED, evaluate transitions,
/// or write evidence. Those authorities remain with Engineering and IoTestSessionController.
/// </summary>
public static class IoFatProductionControllerAdapter
{
    public static IoFatCaptureTargetLease LatchStartTarget(
        IoTestProject project,
        IoTestIedPlan? ied)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (ied == null)
            throw new InvalidOperationException("Select an imported IED first.");
        if (!project.Ieds.Contains(ied))
            throw new InvalidOperationException("The selected IED is no longer part of this FAT project.");

        var preflight = IoTestSessionPreflight.Validate(ied);
        if (!preflight.Succeeded)
            throw new InvalidOperationException(preflight.Message);

        var points = ied.TestPoints
            .Where(point =>
                point.WorkspaceSelected &&
                point.IsIncludedInFat &&
                point.TestEnabled &&
                point.ImportReady)
            .Distinct()
            .Select(point => new IoFatCapturePointLease(
                point,
                IoTestPerIedProgressIdentity.PointConfigurationFingerprint(point)))
            .ToArray();
        if (points.Length == 0)
        {
            throw new InvalidOperationException(
                "No import-ready operator-selected signal is available in the requested FAT capture scope.");
        }

        return new IoFatCaptureTargetLease(
            ied,
            IoTestPerIedProgressIdentity.IedKey(ied),
            ied.LiveDeviceId?.Trim() ?? string.Empty,
            points);
    }

    public static IoTestSessionActionResult StartLatched(
        IoTestProject project,
        IoTestMultiSessionCoordinator sessions,
        IoFatCaptureTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(lease);

        var validation = ValidateLease(project, lease);
        if (!validation.Succeeded)
            return validation;

        // The existing production controller remains the only evidence writer. Passing the
        // frozen scope prevents an async Engineering prepare from silently broadening or
        // redirecting the session when Explorer selection/workspace membership changes.
        return sessions.Start(lease.Ied, lease.CaptureScope);
    }

    public static IoTestSessionActionResult ValidateLease(
        IoTestProject project,
        IoFatCaptureTargetLease lease)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(lease);

        if (!project.Ieds.Contains(lease.Ied))
            return IoTestSessionActionResult.Failure("The latched FAT IED left the project while connection preparation was running. No evidence session was started.");

        var currentIedIdentity = IoTestPerIedProgressIdentity.IedKey(lease.Ied);
        if (!currentIedIdentity.Equals(lease.IedIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return IoTestSessionActionResult.Failure(
                "The latched IEC IED identity or endpoint changed during connection preparation. No evidence session was started.");
        }

        var currentDeviceId = lease.Ied.LiveDeviceId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(lease.DeviceId) &&
            !currentDeviceId.Equals(lease.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return IoTestSessionActionResult.Failure(
                "The Engineering DeviceId bound to the latched FAT IED changed during connection preparation. No evidence session was started.");
        }

        foreach (var pointLease in lease.Points)
        {
            var point = pointLease.Point;
            if (!lease.Ied.TestPoints.Contains(point))
            {
                return IoTestSessionActionResult.Failure(
                    "A latched FAT row left its owning IED during connection preparation. No evidence session was started.");
            }
            if (!point.WorkspaceSelected || !point.IsIncludedInFat || !point.TestEnabled || !point.ImportReady)
            {
                return IoTestSessionActionResult.Failure(
                    $"The latched FAT row '{point.SignalName}' changed eligibility during connection preparation. Review the scope and press Start FAT again.");
            }

            var fingerprint = IoTestPerIedProgressIdentity.PointConfigurationFingerprint(point);
            if (!fingerprint.Equals(pointLease.ConfigurationFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return IoTestSessionActionResult.Failure(
                    $"The IEC/evidence configuration of latched FAT row '{point.SignalName}' changed during connection preparation. No evidence session was started.");
            }
        }

        return IoTestSessionActionResult.Success(
            $"Latched FAT target verified: {lease.Ied.IedName} · {lease.Points.Count} point(s).");
    }
}

public sealed record IoFatCapturePointLease(
    IoTestPointPlan Point,
    string ConfigurationFingerprint);

public sealed record IoFatCaptureTargetLease(
    IoTestIedPlan Ied,
    string IedIdentity,
    string DeviceId,
    IReadOnlyList<IoFatCapturePointLease> Points)
{
    public IReadOnlyList<IoTestPointPlan> CaptureScope { get; } =
        Points.Select(point => point.Point).ToArray();
}
