using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class CanonicalLiveValueSsotM213EStressTests
{
    private static Iec61850PointSnapshot Frame(
        Iec61850MonitorPoint point, long association, long revision, long sequence,
        string value, string quality, string time, string rcb, bool edge = false)
        => new()
        {
            Point = point,
            ObservationAssociation = association,
            SnapshotRevision = revision,
            Sequence = sequence,
            Value = value,
            Quality = quality,
            DeviceTimestamp = time,
            SourceMode = "StaticBRCB",
            IsReportTraffic = true,
            IsValueEdge = edge,
            RoutedReportControlReferences = [rcb]
        };

    [Fact]
    public void MultiRcbMetadataOnlyBurstsRemainOrderedWithoutFabricatingSoe()
    {
        var point = new Iec61850MonitorPoint
        {
            DeviceId = "relay", IecReference = "LD/CSWI1.Pos.stVal",
            FunctionalConstraint = "ST"
        };
        const long association = 5;
        const int burst = 16000;
        var first = Frame(point, association, 1, 3, "Open [01]",
            "Good", "T1", "LD/LLN0.BR.Buffer01");
        var batch = new CanonicalPointSnapshotBatch(first, false);
        for (var i = 2; i <= burst; i++)
        {
            var current = Frame(point, association, i, 3, "Open [01]",
                i % 2 == 0 ? "Good" : "Questionable",
                "T" + i,
                i % 2 == 0 ? "LD/LLN0.BR.Buffer01" : "LD/LLN0.RP.Unbuffer01");
            batch = CanonicalPointSnapshotAuthority.Merge(batch, current);
            // Emulate delayed callbacks from another RCB after a newer frame.
            if (i > 3 && i % 7 == 0)
                batch = CanonicalPointSnapshotAuthority.Merge(batch,
                    Frame(point, association, i - 2, 3, "Open [01]",
                        "Stale", "OLD", "LD/LLN0.RP.Unbuffer01", edge: true));
        }
        Assert.Equal(burst, batch.Snapshot.SnapshotRevision);
        Assert.Equal("T" + burst, batch.Snapshot.DeviceTimestamp);
        Assert.Equal("Good", batch.Snapshot.Quality);
        Assert.False(batch.HasValueEdge);
        Assert.Equal(3, batch.Snapshot.Sequence); // SOE semantics unchanged
    }

    [Fact]
    public void ReconnectStartsNewPublicationOrderEvenWithLowerProcessSequence()
    {
        var point = new Iec61850MonitorPoint
        {
            DeviceId = "relay", IecReference = "LD/CSWI1.Pos.stVal"
        };
        var old = Frame(point, 5, 60000, 400, "Closed [10]",
            "Good", "old", "LD/LLN0.BR.Buffer01", edge: true);
        var newer = Frame(point, 6, 1, 0, "Open [01]",
            "Good", "fresh", "LD/LLN0.BR.Buffer01");
        var batch = new CanonicalPointSnapshotBatch(old, true);
        batch = CanonicalPointSnapshotAuthority.Merge(batch, newer);
        Assert.Same(newer, batch.Snapshot);
        Assert.False(batch.HasValueEdge);
        batch = CanonicalPointSnapshotAuthority.Merge(batch, old);
        Assert.Same(newer, batch.Snapshot);
        Assert.False(batch.HasValueEdge);
        Assert.Same(newer, CanonicalPointSnapshotAuthority.NewerSnapshot(old, newer));
    }

    [Fact]
    public void SequenceOnlyLegacyFallbackAndZeroStampRemainDeterministic()
    {
        var point = new Iec61850MonitorPoint { IecReference = "LD/GGIO1.Ind.stVal" };
        var legacy1 = Frame(point, 0, 0, 1, "false", "-", "-", "Demo");
        var legacy2 = Frame(point, 0, 0, 2, "true", "-", "-", "Demo");
        Assert.Same(legacy2, CanonicalPointSnapshotAuthority.NewerSnapshot(legacy1, legacy2));
        var stamped = Frame(point, 7, 1, 0, "on", "Good", "real", "RCB");
        Assert.Same(stamped, CanonicalPointSnapshotAuthority.NewerSnapshot(legacy2, stamped));
        Assert.Same(stamped, CanonicalPointSnapshotAuthority.NewerSnapshot(stamped, legacy2));
    }

    [Fact]
    public void RuntimeAndWpfMustUsePublicationRevisionButKeepSoeSequenceSeparate()
    {
        static string FileText(string file)
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root is not null)
            {
                var path = Path.Combine(root.FullName, file);
                if (File.Exists(path)) return File.ReadAllText(path);
                root = root.Parent;
            }
            throw new FileNotFoundException(file);
        }

        var runtime = FileText("Services/Iec61850MonitorRuntime.cs");
        var ui = FileText("MainWindow.xaml.cs");
        Assert.Contains("SnapshotRevision = ++state.SnapshotRevision", runtime);
        Assert.Contains("if (changed)\n            state.Sequence++", runtime.Replace("\r\n", "\n"));
        Assert.Contains("CanonicalPointSnapshotAuthority.Merge(current, snapshot)", ui);
        Assert.Contains("snapshot.SnapshotRevision <= point.LastSnapshotRevision", ui);
        Assert.Contains("point.LastSnapshotRevision = snapshot.SnapshotRevision", ui);
        Assert.DoesNotContain("PendingPointUpdate(", ui);
    }
}
