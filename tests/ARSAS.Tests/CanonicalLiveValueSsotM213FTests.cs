using System.Collections.Concurrent;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>M2.13F: same-size model swaps, Read/reconnect and exact IEC identity.</summary>
public sealed class CanonicalLiveValueSsotM213FTests
{
    private static IedBrowserReadResult Read(string reference, string value = "999")
        => new(new IedBrowserReadTarget(reference, "MX", "FLOAT32"),
            value, "", "", "");

    [Fact]
    public void SameCountCatalogReloadCannotUpdateDetachedReadSignalsOrPoints()
    {
        const string reference = "LD/MMXU1.Hz.mag.f";
        var device = new Iec61850MonitorDevice { IsConnected = true };
        var oldSignal = new SignalDefinition { ObjectReference = reference, FunctionalConstraint = "MX" };
        var oldPoint = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId, IecReference = reference,
            FunctionalConstraint = "MX"
        };
        device.Signals.Add(oldSignal);
        device.Points.Add(oldPoint);
        var oldIndex = new IedBrowserReadBindingIndex(device);
        var association = device.LiveValues.Association;
        Assert.True(oldIndex.IsCurrent);
        // Replace with the SAME number of elements and SAME IEC reference.
        // Count-based invalidation would silently retain the detached objects.
        var replacementSignal = new SignalDefinition { ObjectReference = reference, FunctionalConstraint = "MX" };
        var replacementPoint = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId, IecReference = reference,
            FunctionalConstraint = "MX"
        };
        device.Signals.ReplaceAll([replacementSignal]);
        device.Points.ReplaceAll([replacementPoint]);
        Assert.False(oldIndex.IsCurrent);
        Assert.True(device.LiveValues.PublishForAssociation(association, reference, "MX",
            "50", "Good", "T1", "MMS Read"));
        Assert.Equal(0, oldIndex.Apply(Read(reference), device.LiveValues, association));
        Assert.Equal("-", oldSignal.Value);
        Assert.Equal("-", replacementSignal.Value);
        Assert.Equal("-", oldPoint.Value);
        Assert.Equal("-", replacementPoint.Value);

        var current = new IedBrowserReadBindingIndex(device);
        Assert.True(current.IsCurrent);
        Assert.Equal(2, current.Apply(Read(reference), device.LiveValues, association));
        Assert.Equal("50", replacementSignal.Value);
        Assert.Equal("50", replacementPoint.Value);
        Assert.Equal("-", oldSignal.Value);
    }

    [Fact]
    public void SameCountCatalogReloadCannotReanimateOldReportSignal()
    {
        const string reference = "LD/MMXU1.A.phsA.cVal.mag.f";
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var previous = new SignalDefinition { ObjectReference = reference, FunctionalConstraint = "MX" };
        device.Signals.Add(previous);
        var oldIndex = new IedBrowserReadBindingIndex(device);
        var replacement = new SignalDefinition { ObjectReference = reference, FunctionalConstraint = "MX" };
        device.Signals.ReplaceAll([replacement]);
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association, reference, "MX",
            "10", "Good", "T1", "Report"));
        Assert.Equal(0, oldIndex.ProjectReportedSignals(device.LiveValues, association, reference, "MX"));
        Assert.Equal("-", previous.Value);
        Assert.Equal("-", replacement.Value);
        var newIndex = new IedBrowserReadBindingIndex(device);
        Assert.Equal(1, newIndex.ProjectReportedSignals(device.LiveValues, association, reference, "MX"));
        Assert.Equal("10", replacement.Value);
        Assert.Equal("-", previous.Value);
    }

    [Fact]
    public void ExactCaseAndFcRemainDistinctUnderBurstCoalescing()
    {
        var live = new IedBrowserLiveValues();
        var association = live.BeginAssociation();
        var upper = "LD/CSWI1.Pos.stVal";
        var lower = "LD/CSWI1.pos.stVal";
        Assert.True(live.PublishForAssociation(association, upper, "ST", "Open", "", "", "Report"));
        Assert.True(live.PublishForAssociation(association, lower, "ST", "Closed", "", "", "Report"));
        Assert.True(live.PublishForAssociation(association, upper, "MX", "22", "", "", "Report"));
        Assert.True(live.TryGet(upper, "ST", out var a));
        Assert.True(live.TryGet(lower, "ST", out var b));
        Assert.True(live.TryGet(upper, "MX", out var c));
        Assert.Equal("Open", a.Value);
        Assert.Equal("Closed", b.Value);
        Assert.Equal("22", c.Value);

        var batches = new ConcurrentDictionary<string, CanonicalPointSnapshotBatch>(StringComparer.Ordinal);
        foreach (var reference in new[] { upper, lower })
        {
            var point = new Iec61850MonitorPoint { DeviceId = "IED", IecReference = reference, FunctionalConstraint = "ST" };
            var snapshot = new Iec61850PointSnapshot
            {
                Point = point, ObservationAssociation = association,
                SnapshotRevision = 1, Sequence = 1, Value = reference
            };
            batches.AddOrUpdate(CanonicalPointSnapshotAuthority.UiQueueKey(point),
                _ => new CanonicalPointSnapshotBatch(snapshot, false),
                (_, prior) => CanonicalPointSnapshotAuthority.Merge(prior, snapshot));
        }
        Assert.Equal(2, batches.Count);
        var mx = new Iec61850MonitorPoint
        {
            DeviceId = "IED", IecReference = upper, FunctionalConstraint = "MX"
        };
        var st = new Iec61850MonitorPoint
        {
            DeviceId = "IED", IecReference = upper, FunctionalConstraint = "ST"
        };
        Assert.NotEqual(CanonicalPointSnapshotAuthority.UiQueueKey(st),
            CanonicalPointSnapshotAuthority.UiQueueKey(mx));
        Assert.NotEqual(CanonicalPointSnapshotAuthority.UiQueueKey(st),
            CanonicalPointSnapshotAuthority.UiQueueKey(new Iec61850MonitorPoint
            {
                DeviceId = "IED", IecReference = lower, FunctionalConstraint = "ST"
            }));

        // Protect the real WPF receive seam rather than only a test dictionary.
        var source = FindSource("MainWindow.xaml.cs");
        Assert.Contains("_pendingPointSnapshots = new(StringComparer.Ordinal);", source);
        Assert.Contains("CanonicalPointSnapshotAuthority.UiQueueKey(snapshot.Point)", source);
    }

    [Fact]
    public void ReadBindsAfterConnectAndFencesCatalogBeforeEvidenceAdmission()
    {
        var source = FindSource("MainWindow.IedBrowser.cs");
        Assert.Contains("bindingIndex = new IedBrowserReadBindingIndex(sourceDevice);", source);
        Assert.Contains("bindingIndex is not { IsCurrent: true }", source);
    }

    private static string FindSource(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
