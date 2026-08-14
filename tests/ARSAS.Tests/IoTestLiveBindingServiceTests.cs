using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoTestLiveBindingServiceTests
{
    private readonly IoTestLiveBindingService _binding = new();

    [Fact]
    public void ExactDiscoveredSignal_IsBoundToImportedPoint()
    {
        var project = Project("AA1C1F03R4ADD/GGIO6.CBClsd.stVal");
        var device = Device();
        device.Signals.Add(new SignalDefinition
        {
            Name = "CB closed",
            ObjectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST"
        });

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(1, summary.DeviceBoundCount);
        Assert.Equal(1, summary.SignalBoundCount);
        Assert.Equal(0, summary.MissingSignalCount);
        Assert.Equal(IoTestLiveBindingState.BoundExact, project.Ieds[0].TestPoints[0].LiveBindingState);
    }

    [Fact]
    public void IedPrefixDifference_IsNormalizedWhenUnique()
    {
        var project = Project("ADD/GGIO6.CBClsd.stVal");
        var device = Device();
        device.Signals.Add(new SignalDefinition
        {
            Name = "CB closed",
            ObjectReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST"
        });

        _binding.Bind(project, new[] { device });

        Assert.Equal(IoTestLiveBindingState.BoundNormalized, project.Ieds[0].TestPoints[0].LiveBindingState);
    }

    [Fact]
    public void ApplicationFolderHierarchy_IsNormalizedToLiveLnPrefix()
    {
        var project = Project("AA1C1F03R4Application/ADD/GGIO6.CBClsd.stVal");
        var device = Device();
        device.Signals.Add(new SignalDefinition
        {
            Name = "CB closed",
            ObjectReference = "AA1C1F03R4Application/ADDGGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST"
        });

        _binding.Bind(project, new[] { device });

        Assert.Equal(IoTestLiveBindingState.BoundNormalized, project.Ieds[0].TestPoints[0].LiveBindingState);
    }

    [Fact]
    public void PartialTcsLeaf_IsBoundUniquelyToDiscoveredSignal()
    {
        var project = Project(".TCS1Fail");
        var device = Device();
        device.Signals.Add(new SignalDefinition
        {
            Name = "Trip coil monitoring 1",
            ObjectReference = "AA1C1F03R4Application/ADDGGIO2$ST$TCS1Fail$stVal",
            FunctionalConstraint = "ST"
        });

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(1, summary.SignalBoundCount);
        Assert.Equal(0, summary.MissingSignalCount);
        Assert.Equal(IoTestLiveBindingState.BoundNormalized, project.Ieds[0].TestPoints[0].LiveBindingState);
        Assert.Equal(
            "AA1C1F03R4Application/ADDGGIO2$ST$TCS1Fail$stVal",
            project.Ieds[0].TestPoints[0].LiveSignalReference);
    }

    [Fact]
    public void PartialTcsLeaf_DuplicateObjectsRemainUnverified_NotMissing()
    {
        var project = Project(".TCS1Fail");
        var device = Device();
        device.Signals.Add(new SignalDefinition
        {
            Name = "Trip coil monitoring 1 ADD",
            ObjectReference = "AA1C1F03R4Application/ADDGGIO2$ST$TCS1Fail$stVal",
            FunctionalConstraint = "ST"
        });
        device.Signals.Add(new SignalDefinition
        {
            Name = "Trip coil monitoring 1 ALT",
            ObjectReference = "AA1C1F03R4Application/ALTGGIO3$ST$TCS1Fail$stVal",
            FunctionalConstraint = "ST"
        });

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(0, summary.SignalBoundCount);
        Assert.Equal(0, summary.MissingSignalCount);
        Assert.Equal(IoTestLiveBindingState.NotEvaluated, project.Ieds[0].TestPoints[0].LiveBindingState);
        Assert.Contains("no absence conclusion", project.Ieds[0].TestPoints[0].LiveBindingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoLocalCandidate_RemainsUnverified_NotMissing()
    {
        var project = Project("AA1C1F03R4ADD/GGIO6.Unknown.stVal");
        var device = Device();
        device.Signals.Add(new SignalDefinition
        {
            Name = "Different signal",
            ObjectReference = "AA1C1F03R4ADD/GGIO6.Other.stVal",
            FunctionalConstraint = "ST"
        });

        var summary = _binding.Bind(project, new[] { device });

        var point = project.Ieds[0].TestPoints[0];
        Assert.Equal(0, summary.MissingSignalCount);
        Assert.Equal(IoTestLiveBindingState.NotEvaluated, point.LiveBindingState);
        Assert.NotEqual("Signal missing", point.LiveBindingText);
        Assert.Contains("no absence conclusion", point.LiveBindingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActiveLivePoint_PopulatesCurrentEvidencePreview()
    {
        var project = Project("AA1C1F03R4ADD/GGIO6.CBClsd.stVal");
        var device = Device();
        device.Points.Add(new Iec61850MonitorPoint
        {
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            IpAddress = device.IpAddress,
            SignalName = "CB closed",
            IecReference = "AA1C1F03R4ADD/GGIO6.CBClsd.stVal",
            FunctionalConstraint = "ST",
            Value = "True",
            Quality = "Good",
            SourceMode = "BRCB"
        });

        var summary = _binding.Bind(project, new[] { device });

        Assert.Equal(1, summary.LivePointCount);
        var point = project.Ieds[0].TestPoints[0];
        Assert.Equal(IoTestLiveBindingState.LivePointReady, point.LiveBindingState);
        Assert.Equal("True", point.Runtime.CurrentValue);
        Assert.Equal("Good", point.Runtime.CurrentQuality);
        Assert.Equal("BRCB", point.Runtime.CurrentSource);
    }

    [Fact]
    public void MissingWorkspaceDevice_IsExplicitlyReported()
    {
        var project = Project("AA1C1F03R4ADD/GGIO6.CBClsd.stVal");

        var summary = _binding.Bind(project, Array.Empty<Iec61850MonitorDevice>());

        Assert.Equal(0, summary.DeviceBoundCount);
        Assert.Equal(IoTestLiveBindingState.DeviceNotLoaded, project.Ieds[0].TestPoints[0].LiveBindingState);
        Assert.Contains("load or connect", project.Ieds[0].TestPoints[0].LiveBindingReason.ToLowerInvariant());
    }

    [Fact]
    public void EngineAbsent_IsTheOnlyPresentationThatMapsToSignalNotFound()
    {
        var absent = EnginePoint(Iec61850DesignLiveStatus.Absent);

        Assert.Equal(
            IoTestLiveBindingState.SignalNotFound,
            IoTestReconciliationPresentation.FromEnginePoint(absent).State);
        Assert.True(IoTestReconciliationPresentation.FromEnginePoint(absent).IsConfirmedAbsent);

        var diagnosticStatuses = new[]
        {
            Iec61850DesignLiveStatus.DesignOnly,
            Iec61850DesignLiveStatus.InvalidTarget,
            Iec61850DesignLiveStatus.Unreadable,
            Iec61850DesignLiveStatus.TransportFailure,
            Iec61850DesignLiveStatus.FunctionalConstraintMismatch,
            Iec61850DesignLiveStatus.TypeMismatch,
            Iec61850DesignLiveStatus.Ambiguous,
            Iec61850DesignLiveStatus.UnresolvedDesign,
            Iec61850DesignLiveStatus.LiveOnly
        };

        foreach (var status in diagnosticStatuses)
        {
            var presentation = IoTestReconciliationPresentation.FromEnginePoint(EnginePoint(status));
            Assert.Equal(IoTestLiveBindingState.NotEvaluated, presentation.State);
            Assert.False(presentation.IsConfirmedAbsent);
        }
    }

    [Fact]
    public void EngineRecoveredByProbe_IsPresentedAsVerifiedBinding()
    {
        var point = new Iec61850DesignLivePointReconciliation
        {
            Reference = "IEDLD/GGIO1.Test.stVal",
            MmsReference = "IEDLD/GGIO1$ST$Test$stVal",
            CanonicalMmsReference = "IEDLD/GGIO1$ST$Test$stVal",
            EffectiveMmsReference = "IEDLD/GGIO1$ST$Test$stVal",
            FunctionalConstraint = "ST",
            Status = Iec61850DesignLiveStatus.RecoveredByProbe,
            Probe = new Iec61850ExactProbeEvidence
            {
                Status = Iec61850ExactProbeStatus.Readable,
                MmsReference = "IEDLD/GGIO1$ST$Test$stVal",
                FunctionalConstraint = "ST",
                ValueSummary = "true",
                Message = "Exact read succeeded."
            },
            Evidence = new[] { "Recovered by engine exact probe." }
        };

        var presentation = IoTestReconciliationPresentation.FromEnginePoint(point);

        Assert.Equal(IoTestLiveBindingState.BoundExact, presentation.State);
        Assert.False(presentation.IsConfirmedAbsent);
        Assert.Contains("RecoveredByProbe", presentation.Reason, StringComparison.Ordinal);
        Assert.Contains("Canonical:", presentation.Reason, StringComparison.Ordinal);
        Assert.Contains("Effective:", presentation.Reason, StringComparison.Ordinal);
        Assert.Contains("Readable", presentation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EngineRecoveredByAlternateProbe_PresentsCanonicalEffectiveAndAttempts()
    {
        const string canonical = "IEDLD/MMXU1$MX$TotW$mag$f";
        const string effective = "IEDLD/MMXU1$MX$TotW$instMag$f";
        var point = new Iec61850DesignLivePointReconciliation
        {
            Reference = "IEDLD/MMXU1.TotW.mag.f",
            MmsReference = canonical,
            CanonicalMmsReference = canonical,
            EffectiveMmsReference = effective,
            FunctionalConstraint = "MX",
            Status = Iec61850DesignLiveStatus.RecoveredByAlternateProbe,
            Probe = new Iec61850ExactProbeEvidence
            {
                Status = Iec61850ExactProbeStatus.Readable,
                MmsReference = effective,
                FunctionalConstraint = "MX",
                ValueSummary = "123.4",
                Message = "Alternate exact read succeeded."
            },
            ProbeAttempts = new Iec61850ProbeAttemptEvidence[]
            {
                new()
                {
                    IsCanonical = true,
                    Explanation = "Canonical MMS target.",
                    Probe = new Iec61850ExactProbeEvidence
                    {
                        Status = Iec61850ExactProbeStatus.Absent,
                        MmsReference = canonical,
                        FunctionalConstraint = "MX",
                        FailureCode = 4,
                        Message = "object-undefined"
                    }
                },
                new()
                {
                    IsCanonical = false,
                    AlternateStrategy = Iec61850AlternateReferenceStrategyKind.MagnitudeInstantaneousSibling,
                    Explanation = "IEC 61850 measurement sibling mag.f -> instMag.f.",
                    Probe = new Iec61850ExactProbeEvidence
                    {
                        Status = Iec61850ExactProbeStatus.Readable,
                        MmsReference = effective,
                        FunctionalConstraint = "MX",
                        ValueSummary = "123.4",
                        Message = "Alternate exact read succeeded."
                    }
                }
            },
            Evidence = new[] { "Recovered by bounded engine alternate probing." }
        };

        var presentation = IoTestReconciliationPresentation.FromEnginePoint(point);

        Assert.Equal(IoTestLiveBindingState.BoundNormalized, presentation.State);
        Assert.False(presentation.IsConfirmedAbsent);
        Assert.Equal(effective, presentation.Reference);
        Assert.Contains("RecoveredByAlternateProbe", presentation.Reason, StringComparison.Ordinal);
        Assert.Contains($"Canonical: {canonical}", presentation.Reason, StringComparison.Ordinal);
        Assert.Contains($"Effective: {effective}", presentation.Reason, StringComparison.Ordinal);
        Assert.Contains("MagnitudeInstantaneousSibling", presentation.Reason, StringComparison.Ordinal);
        Assert.Contains("Probe attempt 2", presentation.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("engine failure code: 4", presentation.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CacheCold_BindingDoesNotProduceReconciliationSynchronously()
    {
        var project = Project("AA1C1F03R4ADD/GGIO6.Unknown.stVal");
        var device = DeviceWithModels();
        IoTestReconciliationCache.Invalidate(device);

        var before = IoTestReconciliationCache.Get(device);
        _binding.Bind(project, new[] { device });
        var after = IoTestReconciliationCache.Get(device);

        Assert.False(before.IsCurrent);
        Assert.False(after.IsCurrent);
        Assert.Null(after.Document);
        Assert.Contains("cache is not ready", after.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AsyncRefresh_PublishesReconciliationForExactModelGeneration()
    {
        var device = DeviceWithModels();
        IoTestReconciliationCache.Invalidate(device);

        await IoTestReconciliationCache.RefreshAsync(device);
        var cached = IoTestReconciliationCache.Get(device);

        Assert.True(cached.IsCurrent);
        Assert.NotNull(cached.Document);
        Assert.NotNull(cached.ProducedAtUtc);
        Assert.Equal(string.Empty, cached.FailureReason);
    }

    private static Iec61850DesignLivePointReconciliation EnginePoint(Iec61850DesignLiveStatus status)
        => new()
        {
            Reference = "IEDLD/GGIO1.Test.stVal",
            MmsReference = "IEDLD/GGIO1$ST$Test$stVal",
            CanonicalMmsReference = "IEDLD/GGIO1$ST$Test$stVal",
            EffectiveMmsReference = "IEDLD/GGIO1$ST$Test$stVal",
            FunctionalConstraint = "ST",
            Status = status,
            Evidence = new[] { $"Engine status: {status}" }
        };

    private static IoTestProject Project(string reference)
    {
        var project = new IoTestProject
        {
            ProjectId = "CCPP-260728",
            SchemaVersion = "ARSAS-FAT-IO-1.0",
            ProjectName = "CCPP FAT",
            Ieds =
            {
                new IoTestIedPlan
                {
                    IedName = "AA1C1F03R4",
                    IpAddress = "192.168.81.70",
                    IedRole = "BCU - 6MD85",
                    TestPoints = { Point(reference) }
                }
            }
        };
        project.InitializeRuntimeNotifications();
        return project;
    }

    private static IoTestPointPlan Point(string reference) => new()
    {
        TestPointId = "TP-001",
        IedName = "AA1C1F03R4",
        IpAddress = "192.168.81.70",
        SignalName = "CB closed",
        ObjectReference = reference,
        LogicalNode = "GGIO6",
        FunctionalConstraint = "ST",
        ExpectedOnText = "Active",
        ExpectedOffText = "InActive",
        ImportReady = true,
        BindingStatus = "CID_DATASET_EXACT"
    };

    private static Iec61850MonitorDevice Device() => new()
    {
        Name = "AA1C1F03R4",
        SclIedName = "AA1C1F03R4",
        IpAddress = "192.168.81.70",
        Port = 102,
        Status = "Ready"
    };

    private static Iec61850MonitorDevice DeviceWithModels()
    {
        var designModel = new LiveIedModelDiscoveryDocument
        {
            IedName = "AA1C1F03R4"
        };
        var liveModel = new LiveIedModelDiscoveryDocument
        {
            IedName = "AA1C1F03R4"
        };

        return new Iec61850MonitorDevice
        {
            Name = "AA1C1F03R4",
            SclIedName = "AA1C1F03R4",
            IpAddress = "192.168.81.70",
            Port = 102,
            Status = "Ready",
            SclWorkspace = new SclIedWorkspace
            {
                IedName = "AA1C1F03R4",
                DesignModel = designModel
            },
            LiveDiscoveryModel = liveModel
        };
    }
}
