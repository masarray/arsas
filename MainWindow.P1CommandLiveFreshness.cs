using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Relay-bench freshness authority at the runtime -> Engineering/FAT presentation boundary.
/// A command-confirmed CSWI/XCBR position is published immediately. Until a matching report
/// confirms that command (or the bounded two-second fence expires), a contradictory report
/// cannot roll the shared Engineering process image back to the pre-command position. Once a
/// matching report is seen, later contradictory report traffic is again authoritative.
///
/// This is not a visual debounce: rejected snapshots never enter MainWindow's coalesced point
/// image, FAT LIVE projection, or SOE queue. Evidence therefore cannot be created from the
/// transient stale value either.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan P1CommandLiveFreshnessWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan P1SuppressedEventOriginWindow = TimeSpan.FromSeconds(1);

    private sealed record P1CommandLiveFence(
        string ExpectedValue,
        bool MatchingReportSeen,
        DateTime ExpiresUtc);

    private sealed record P1SuppressedEventOrigin(
        string Value,
        DateTime ExpiresUtc);

    private readonly ConcurrentDictionary<string, P1CommandLiveFence> _p1CommandLiveFences =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, P1SuppressedEventOrigin> _p1SuppressedCommandEvents =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _p1CommandLiveFreshnessInstalled;

    [ModuleInitializer]
    internal static void RegisterP1CommandLiveFreshnessAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P1CommandLiveFreshness_MainLoaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P1CommandLiveFreshness_FatLoaded),
            handledEventsToo: true);
    }

    private static void P1CommandLiveFreshness_MainLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.InstallP1CommandLiveFreshnessFilter();
    }

    private static void P1CommandLiveFreshness_FatLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow fat || fat.Owner is not MainWindow engineering)
            return;

        // P0 shared-process setup intentionally rewires raw FAT listeners. Re-apply this
        // boundary after every FAT window has completed Loaded so there remains exactly one
        // filtered runtime path into both Engineering and FAT presentation.
        engineering.Dispatcher.BeginInvoke(
            new Action(engineering.InstallP1CommandLiveFreshnessFilter),
            DispatcherPriority.ApplicationIdle);
    }

    private void InstallP1CommandLiveFreshnessFilter()
    {
        // Runtime_PointUpdated is MainWindow's Engineering image feed. P0FatRuntimePointUpdated
        // is the presentation-only FAT mirror. Both must consume the same accepted snapshots.
        _runtime.PointUpdated -= Runtime_PointUpdated;
        _runtime.PointUpdated -= P0FatRuntimePointUpdated;
        _runtime.PointUpdated -= P1CommandLiveFreshness_PointUpdated;
        _runtime.PointUpdated += P1CommandLiveFreshness_PointUpdated;

        _runtime.EventRaised -= Runtime_EventRaised;
        _runtime.EventRaised -= P1CommandLiveFreshness_EventRaised;
        _runtime.EventRaised += P1CommandLiveFreshness_EventRaised;

        if (_p1CommandLiveFreshnessInstalled)
            return;

        _p1CommandLiveFreshnessInstalled = true;
        Closed += P1CommandLiveFreshness_Closed;
    }

    private void P1CommandLiveFreshness_PointUpdated(Iec61850PointSnapshot snapshot)
    {
        var key = P1CommandLiveFreshnessKey(snapshot.Point.DeviceId, snapshot.Point.IecReference);
        var nowUtc = DateTime.UtcNow;

        if (P1IsConfirmedCommandFeedback(snapshot))
        {
            _p1CommandLiveFences[key] = new P1CommandLiveFence(
                snapshot.Value?.Trim() ?? string.Empty,
                MatchingReportSeen: false,
                nowUtc.Add(P1CommandLiveFreshnessWindow));
            P1PublishAcceptedPointSnapshot(snapshot);
            return;
        }

        if (!_p1CommandLiveFences.TryGetValue(key, out var fence))
        {
            P1PublishAcceptedPointSnapshot(snapshot);
            return;
        }

        if (nowUtc > fence.ExpiresUtc)
        {
            _p1CommandLiveFences.TryRemove(key, out _);
            P1PublishAcceptedPointSnapshot(snapshot);
            return;
        }

        var matchesExpected = P1CommandValuesEquivalent(fence.ExpectedValue, snapshot.Value);
        var decision = P1DecideCommandFreshnessForTest(
            fence.MatchingReportSeen,
            snapshot.IsReportTraffic,
            matchesExpected);

        switch (decision)
        {
            case P1CommandFreshnessDecision.ConfirmAndPublish:
                _p1CommandLiveFences[key] = fence with { MatchingReportSeen = true };
                P1PublishAcceptedPointSnapshot(snapshot);
                return;

            case P1CommandFreshnessDecision.ReleaseAndPublish:
                _p1CommandLiveFences.TryRemove(key, out _);
                P1PublishAcceptedPointSnapshot(snapshot);
                return;

            case P1CommandFreshnessDecision.Publish:
                P1PublishAcceptedPointSnapshot(snapshot);
                return;

            case P1CommandFreshnessDecision.Suppress:
                if (snapshot.IsValueEdge)
                {
                    _p1SuppressedCommandEvents[key] = new P1SuppressedEventOrigin(
                        snapshot.Value?.Trim() ?? string.Empty,
                        nowUtc.Add(P1SuppressedEventOriginWindow));
                }
                Trace.WriteLine(
                    $"[P1 COMMAND FRESHNESS] Suppressed stale process image {snapshot.Point.IecReference}={snapshot.Value}; expected={fence.ExpectedValue}; report={snapshot.IsReportTraffic}; matchingReportSeen={fence.MatchingReportSeen}.");
                return;
        }
    }

    private void P1PublishAcceptedPointSnapshot(Iec61850PointSnapshot snapshot)
    {
        Runtime_PointUpdated(snapshot);
        P0FatRuntimePointUpdated(snapshot);
    }

    private void P1CommandLiveFreshness_EventRaised(Iec61850EventEntry entry)
    {
        var key = P1CommandLiveFreshnessKey(entry.DeviceId, entry.IecReference);
        if (_p1SuppressedCommandEvents.TryGetValue(key, out var suppressed))
        {
            if (DateTime.UtcNow <= suppressed.ExpiresUtc &&
                P1CommandValuesEquivalent(suppressed.Value, entry.NewValue))
            {
                _p1SuppressedCommandEvents.TryRemove(key, out _);
                Trace.WriteLine(
                    $"[P1 COMMAND FRESHNESS] Suppressed SOE paired with rejected stale process image {entry.IecReference}={entry.NewValue}.");
                return;
            }

            if (DateTime.UtcNow > suppressed.ExpiresUtc)
                _p1SuppressedCommandEvents.TryRemove(key, out _);
        }

        Runtime_EventRaised(entry);
    }

    private static bool P1IsConfirmedCommandFeedback(Iec61850PointSnapshot snapshot)
        => snapshot.IsValueEdge &&
           !snapshot.IsReportTraffic &&
           (snapshot.Reason ?? string.Empty).Contains(
               "confirmed command feedback",
               StringComparison.OrdinalIgnoreCase);

    internal static P1CommandFreshnessDecision P1DecideCommandFreshnessForTest(
        bool matchingReportSeen,
        bool isReportTraffic,
        bool matchesExpected)
    {
        if (matchesExpected)
            return isReportTraffic && !matchingReportSeen
                ? P1CommandFreshnessDecision.ConfirmAndPublish
                : P1CommandFreshnessDecision.Publish;

        if (isReportTraffic && matchingReportSeen)
            return P1CommandFreshnessDecision.ReleaseAndPublish;

        return P1CommandFreshnessDecision.Suppress;
    }

    private static string P1CommandLiveFreshnessKey(string? deviceId, string? reference)
        => $"{(deviceId ?? string.Empty).Trim().ToLowerInvariant()}|{(reference ?? string.Empty).Trim().Replace('$', '.').ToLowerInvariant()}";

    private static bool P1CommandValuesEquivalent(string? left, string? right)
    {
        var leftText = (left ?? string.Empty).Trim();
        var rightText = (right ?? string.Empty).Trim();
        if (leftText.Equals(rightText, StringComparison.OrdinalIgnoreCase))
            return true;

        if (bool.TryParse(leftText, out var leftBool) &&
            bool.TryParse(rightText, out var rightBool))
        {
            return leftBool == rightBool;
        }

        var leftCode = P1ExtractStateCode(leftText);
        var rightCode = P1ExtractStateCode(rightText);
        return leftCode.Length > 0 &&
               rightCode.Length > 0 &&
               leftCode.Equals(rightCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string P1ExtractStateCode(string value)
    {
        var open = value.LastIndexOf('[');
        var close = value.LastIndexOf(']');
        return open >= 0 && close > open
            ? value[(open + 1)..close].Trim()
            : string.Empty;
    }

    private void P1CommandLiveFreshness_Closed(object? sender, EventArgs e)
    {
        Closed -= P1CommandLiveFreshness_Closed;
        _runtime.PointUpdated -= P1CommandLiveFreshness_PointUpdated;
        _runtime.EventRaised -= P1CommandLiveFreshness_EventRaised;
        _p1CommandLiveFences.Clear();
        _p1SuppressedCommandEvents.Clear();
        _p1CommandLiveFreshnessInstalled = false;
    }
}

internal enum P1CommandFreshnessDecision
{
    Publish,
    Suppress,
    ConfirmAndPublish,
    ReleaseAndPublish
}
