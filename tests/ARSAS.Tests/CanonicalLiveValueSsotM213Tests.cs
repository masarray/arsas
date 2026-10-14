using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class CanonicalLiveValueSsotM213Tests
{
    [Fact]
    public void ReportIngressIsOneCanonicalAuthorityForBrowserExplorerAndLiveMonitor()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var point = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId, IecReference = "LD/CSWI1.Pos.stVal",
            FunctionalConstraint = "ST", IecDataType = "Dbpos",
            Category = "Position"
        };
        var association = device.LiveValues.Association;
        Assert.True(device.LiveValues.PublishForAssociation(association,
            point.IecReference, "ST", "1", "", "", "Report"));
        var report = new Iec61850PointSnapshot
        {
            Point = point, Value = "different stale UI snapshot",
            SourceMode = "StaticBRCB", IsReportTraffic = true,
            ObservationAssociation = association
        };
        Assert.Equal("Open [01]",
            CanonicalPointSnapshotAuthority.ProcessDisplay(device, point, report));
        Assert.True(device.LiveValues.TryGet(point.IecReference, "ST", out var observed));
        Assert.Equal("1", observed.Value); // rendering never changes canonical evidence

        Assert.True(device.LiveValues.PublishForAssociation(association,
            point.IecReference, "ST", "2", "", "", "MMS Read"));
        Assert.Equal("Open [01]",
            CanonicalPointSnapshotAuthority.ProcessDisplay(device, point, report));

        device.IsMonitoring = false;
        Assert.Equal(report.Value,
            CanonicalPointSnapshotAuthority.ProcessDisplay(device, point, report));
    }

    [Fact]
    public void WrongFcUnobservedPointAndOldAssociationNeverBorrowValue()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true, IsMonitoring = true };
        var point = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId, IecReference = "LD/MMXU1.Hz.mag.f",
            FunctionalConstraint = "MX", IecDataType = "FLOAT32",
            Unit = "Hz"
        };
        var association = device.LiveValues.Association;
        var snapshot = new Iec61850PointSnapshot
        {
            Point = point, ObservationAssociation = association,
            Value = "unobserved", IsReportTraffic = true
        };
        Assert.True(device.LiveValues.PublishForAssociation(association,
            point.IecReference, "ST", "90", "", "", "Report"));
        Assert.Equal("unobserved",
            CanonicalPointSnapshotAuthority.ProcessDisplay(device, point, snapshot));
        Assert.True(device.LiveValues.PublishForAssociation(association,
            point.IecReference, "MX", "50", "", "", "Report"));
        Assert.Equal("50 Hz",
            CanonicalPointSnapshotAuthority.ProcessDisplay(device, point, snapshot));
        device.IsConnected = false; // invalidate old read and report association
        device.IsConnected = true;
        Assert.NotEqual(association, device.LiveValues.Association);
        Assert.Equal("unobserved",
            CanonicalPointSnapshotAuthority.ProcessDisplay(device, point, snapshot));
    }

    [Fact]
    public void CoalescingNeverAllowsOldSessionSequenceToOverrideNewAssociation()
    {
        var point = new Iec61850MonitorPoint { IecReference = "LD/CSWI1.Pos.stVal" };
        var old = new Iec61850PointSnapshot
        {
            Point = point, ObservationAssociation = 4,
            Sequence = 700, Value = "old"
        };
        var fresh = new Iec61850PointSnapshot
        {
            Point = point, ObservationAssociation = 5,
            Sequence = 1, Value = "fresh"
        };
        Assert.Same(fresh, CanonicalPointSnapshotAuthority.NewerSnapshot(old, fresh));
        Assert.Same(fresh, CanonicalPointSnapshotAuthority.NewerSnapshot(fresh, old));
        Assert.Same(old, CanonicalPointSnapshotAuthority.NewerSnapshot(old,
            new Iec61850PointSnapshot {
                Point = point, ObservationAssociation = 4,
                Sequence = 699
            }));
    }

    [Fact]
    public void HistoricRcbMembershipIsNotProofOfCurrentReportIngress()
    {
        var device = new Iec61850MonitorDevice { IsConnected = true };
        var point = new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            IecReference = "LD/CSWI1.Pos.stVal",
            FunctionalConstraint = "ST",
            Value = "Close [10]", SourceMode = "MMS Polling",
            RoutedReportControlReferences = ["LD/LLN0.BR.Buffer01"]
        };
        device.LiveValues.PublishReport(point);
        Assert.False(device.LiveValues.TryGet(point.IecReference, "ST", out _));

        device.LiveValues.PublishReport(point, observedReportIngress: true);
        Assert.True(device.LiveValues.TryGet(point.IecReference, "ST", out var reported));
        Assert.Equal("Report", reported.Source);
        Assert.Equal("Close [10]", reported.Value);

        // A later non-Report snapshot must never overwrite prior authority.
        point.Value = "Open [01]";
        device.LiveValues.PublishReport(point);
        Assert.True(device.LiveValues.TryGet(point.IecReference, "ST", out reported));
        Assert.Equal("Close [10]", reported.Value);
    }

    [Fact]
    public void RuntimeAndUiContractStampsAssociationAndUsesCanonicalProjection()
    {
        static string Read(string filename)
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir != null)
            {
                var path = Path.Combine(dir.FullName, filename);
                if (File.Exists(path)) return File.ReadAllText(path);
                dir = dir.Parent;
            }
            throw new FileNotFoundException(filename);
        }
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var ui = Read("MainWindow.xaml.cs");
        Assert.Contains("ObservationAssociation = session.BrowserObservationAssociation", runtime);
        Assert.Contains("CanonicalPointSnapshotAuthority.ProcessDisplay(", ui);
        Assert.Contains("snapshot.ObservationAssociation != observedOwner.LiveValues.Association", ui);
        Assert.Contains("CanonicalPointSnapshotAuthority.NewerSnapshot(", ui);
    }
}
