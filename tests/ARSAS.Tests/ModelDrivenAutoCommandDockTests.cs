using System.Collections.Specialized;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>Control service evidence -> read-only typed proof -> stable Dock.
/// Never generates a physical MMS control request.</summary>
public sealed class ModelDrivenAutoCommandDockTests
{
    private static SignalDefinition Candidate(string reference, string cdc = "DPC",
        bool withCo = true)
        => new()
        {
            ObjectReference = reference, Name = reference,
            FunctionalConstraint = "CO", IsControlSignal = true,
            HasControlServiceEvidence = withCo, IsSelected = false,
            ControlCdc = cdc, ControlModelText = "Auto-detect"
        };

    private static void Verify(SignalDefinition signal, string cdc)
    {
        signal.ControlModelText = "Select Before Operate (SBO) • Enhanced security";
        signal.AcceptVerifiedLiveControlCdc(cdc);
        signal.ControlSemanticVerified = true;
    }

    [Theory]
    [InlineData("LD/Bkr0XCBR2.Pos", "DPC")]
    [InlineData("LD/Disc0XSWI1.Pos", "DPC")]
    [InlineData("LD/BkrCSWI1.Pos", "DPC")]
    [InlineData("LD/AnyRelayLNode1.TapChg", "BSC")]
    [InlineData("LD/GGIO1.Enable", "SPC")]
    [InlineData("LD/AVCO1.SetV", "APC")]
    public void ModelEvidenceNotLnNames_SelectsAllClassesForReadOnlyInspection(
        string reference, string cdc)
    {
        var candidate = Candidate(reference, cdc);
        var selected = Iec61850ControlIdentity.DiscoveredControlInspectionCandidates([candidate]);
        Assert.Same(candidate, Assert.Single(selected));
        Assert.False(candidate.ControlSupportsOperate);
        Assert.Empty(Iec61850ControlIdentity.VerifiedCommandTargets([candidate], true));
    }

    [Fact]
    public void LiveControlProbePrioritizesEquipmentBeforeLargeGenericChannelBank()
    {
        var bank = Enumerable.Range(1, 900)
            .Select(i => Candidate($"SG7_BCUMaster/GGIO2.DPCSO{i:000}"))
            .ToList();
        var breaker = Candidate("SG7_BCUSystem/Bkr0XCBR1.Pos");
        var switchController = Candidate("SG7_BCUSystem/BkrCSWI1.Pos");
        var disconnector = Candidate("SG7_BCUSystem/Disc0XSWI1.Pos");
        var tap = Candidate("CustomLD/ATCC1.TapChg", "BSC");
        // Deliberately put hundreds of generic channels first.
        var candidates = bank.Concat([switchController, tap, disconnector, breaker]).ToArray();
        var scheduled = Iec61850ControlIdentity.DiscoveredControlInspectionCandidates(candidates);
        Assert.Equal(new[] { switchController, breaker, disconnector, tap },
            scheduled.Take(4));
        Assert.Equal(904, scheduled.Length);
        Assert.All(scheduled, signal => Assert.False(signal.ControlSupportsOperate));
        Assert.Empty(Iec61850ControlIdentity.VerifiedCommandTargets(scheduled, true));
    }

    [Fact]
    public void NoCoCfProofMeansNoAutomaticCommandEvenWithPosOrTapChgName()
    {
        var ghosts = new[]
        {
            Candidate("LD/Bkr0XCBR2.Pos", withCo: false),
            Candidate("LD/ATCC1.TapChg", "BSC", withCo: false),
            Candidate("LD/CSWI1.Pos", withCo: false)
        };
        Assert.Empty(Iec61850ControlIdentity.DiscoveredControlInspectionCandidates(ghosts));
        foreach (var signal in ghosts) Verify(signal, signal.ControlCdc);
        Assert.Empty(Iec61850ControlIdentity.VerifiedCommandTargets(ghosts, true));
    }

    [Fact]
    public void VerifiedAutomaticDockIncludesTapAndPrefixedSwitch_WithoutUserSelection()
    {
        var breaker = Candidate("SG7_BCUSystem/Bkr0XCBR2.Pos");
        var tap = Candidate("OTHER/UnusualLn1.TapChg", "BSC");
        var statusOnly = Candidate("SG7_BCUSystem/Disc0XSWI2.Pos");
        statusOnly.ControlModelText = "Status only";
        var noProof = Candidate("SG7_BCUSystem/Bkr0XCBR1.Pos", withCo: false);
        Verify(breaker, "DPC");
        Verify(tap, "BSC");
        Verify(noProof, "DPC");

        var device = new Iec61850MonitorDevice { IsConnected = true,
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument() };
        device.Signals.ReplaceAll([breaker, tap, statusOnly, noProof]);
        device.RecountSelectedSignals();

        Assert.Equal(2, device.OperableCommandSignals.Count);
        Assert.Contains(breaker, device.OperableCommandSignals);
        Assert.Contains(tap, device.OperableCommandSignals);
        Assert.DoesNotContain(statusOnly, device.OperableCommandSignals);
        Assert.DoesNotContain(noProof, device.OperableCommandSignals);
        Assert.False(breaker.IsSelected);
        Assert.False(tap.IsSelected);

        var events = new List<NotifyCollectionChangedAction>();
        device.OperableCommandSignals.CollectionChanged += (_, e) => events.Add(e.Action);
        var first = device.OperableCommandSignals.ToArray();
        for (var i = 0; i < 100; i++)
        {
            breaker.ControlCurrentValue = i % 2 == 0 ? "Open" : "Closed";
            tap.ControlInspectionEvidence = "MMS read-only status refreshed";
            device.RefreshCommandSignalProjection();
        }
        Assert.Empty(events);
        Assert.True(first.SequenceEqual(device.OperableCommandSignals));
    }

    [Fact]
    public void DuplicateControlReference_UsesVerifiedOwnerRegardlessOfSelection()
    {
        var staleSelected = Candidate("LD/BkrCSWI1.Pos");
        staleSelected.IsSelected = true;
        var live = Candidate("LD/BkrCSWI1.Pos");
        Verify(live, "DPC");
        Assert.Same(live, Assert.Single(Iec61850ControlIdentity.VerifiedCommandTargets(
            [staleSelected, live], true)));
    }

    [Fact]
    public void Ed2VerifiedEquipmentAppearsBeforeGenericIoWithoutInventingWiring()
    {
        var io = Candidate("SG7_BCUMaster/GGIO2.DPCSO01");
        var breaker1 = Candidate("SG7_BCUSystem/Bkr0XCBR1.Pos");
        var breaker2 = Candidate("SG7_BCUSystem/Bkr0XCBR2.Pos");
        var cswi = Candidate("SG7_BCUSystem/BkrCSWI1.Pos");
        var cilo = Candidate("SG7_BCUSystem/BkrCILO1.EnaOpn", "SPC", withCo: false);
        var disconnector = Candidate("SG7_BCUSystem/Disc0XSWI1.Pos");
        var tap = Candidate("OtherLD/ATCC1.TapChg", "BSC");
        var unknown = Candidate("OtherLD/OddVendor1.Pos");
        var statusOnly = Candidate("SG7_BCUSystem/Bkr0XCBR3.Pos");
        statusOnly.ControlModelText = "Status only";
        cilo.ControlModelText = "Status only";

        var signals = new[] { io, breaker1, breaker2, cswi, cilo,
            disconnector, tap, unknown, statusOnly };
        foreach (var signal in signals.Where(s =>
            !ReferenceEquals(s, statusOnly) && !ReferenceEquals(s, cilo)))
            Verify(signal, signal.ControlCdc);

        var device = new Iec61850MonitorDevice
        {
            IsConnected = true,
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument()
        };
        device.Signals.ReplaceAll(signals);
        device.RecountSelectedSignals();

        var dock = device.OperableCommandSignals.ToArray();
        Assert.Equal(7, dock.Length);
        Assert.Equal(new[] { cswi, breaker1, breaker2, disconnector, tap,
            unknown, io }, dock);
        Assert.Equal("Circuit breaker · XCBR",
            Iec61850ControlIdentity.CommandDockRole(breaker1));
        Assert.Equal("Generic I/O · equipment unlinked",
            Iec61850ControlIdentity.CommandDockRole(io));
        Assert.DoesNotContain(statusOnly, dock);
        Assert.DoesNotContain(cilo, dock);
        // Status ownership and equipment linkage are not guessed from rank:
        // the independent CSWI command and breaker remain separate references.
        Assert.NotEqual(breaker1.ObjectReference, cswi.ObjectReference);

        var events = new List<NotifyCollectionChangedAction>();
        device.OperableCommandSignals.CollectionChanged += (_, args) => events.Add(args.Action);
        for (var i = 0; i < 100; i++)
        {
            breaker1.ControlCurrentValue = i % 2 == 0 ? "Open" : "Closed";
            device.RefreshCommandSignalProjection();
        }
        Assert.Empty(events);
        Assert.Equal(dock, device.OperableCommandSignals);
    }

    [Fact]
    public void RawMmsNameListCreatesCandidateOnlyForExactCoCfService()
    {
        var snapshot = new NativeMmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>
            {
                ["LD"] = [
                    "Bkr0XCBR2$ST$Pos$stVal",
                    "Disc0XSWI1$ST$Pos$stSeld",
                    "ATCC1$ST$TapChg$valWTr$posVal",
                    "Bkr0XCBR2$CF$Pos$ctlModel",
                    "ATCC1$CO$TapChg$Oper$ctlVal",
                    "GGIO1$CO$SetPoint$SBOw$ctlVal"
                ]
            }
        };
        var signals = NativeMmsDiscoveryMapper.BuildSignals(snapshot);
        var controls = signals.Where(s => s.IsControlSignal).ToArray();
        Assert.Contains(controls, s => s.ObjectReference == "LD/Bkr0XCBR2.Pos" &&
                                        s.HasControlServiceEvidence);
        Assert.Contains(controls, s => s.ObjectReference == "LD/ATCC1.TapChg" &&
                                        s.HasControlServiceEvidence);
        Assert.Contains(controls, s => s.ObjectReference == "LD/GGIO1.SetPoint" &&
                                        s.HasControlServiceEvidence);
        Assert.DoesNotContain(controls, s => s.ObjectReference == "LD/Disc0XSWI1.Pos");
        Assert.All(controls, s => Assert.False(s.ControlSupportsOperate));
        Assert.All(controls, s => Assert.True(string.IsNullOrEmpty(s.VerifiedLiveControlCdc)));
    }
}
