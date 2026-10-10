using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void BuildDemoGooseWorkspace()
    {
        GooseAdapters.ReplaceAll(new[]
        {
            new GooseAdapterOption
            {
                Index = 1,
                Name = "station-bus-ethernet-1",
                FriendlyName = "Intel(R) Ethernet Connection I219-LM - Station Bus",
                Description = "Intel(R) Ethernet Connection (7) I219-LM",
                MacAddress = "02:00:5E:61:85:00"
            }
        });
        SelectedGooseAdapter = GooseAdapters[0];
        IsGooseCapturing = true;
        GooseBindingText = "6 publishers • ordered DataSet leaves resolved from SCL and live MMS models";
        GooseStatusText = "Receiving GOOSE • 6 publishers • sequence and TAL supervision active.";

        var streamSpecs = new[]
        {
            DemoGooseSpec("gcb-incomer", "E02BCU1", "0x1001", "01:0C:CD:01:00:01", 100, "gcbTripStatus", "dsTripStatus",
                ("Breaker position", "XCBR1.Pos.stVal", "DPC / Dbpos", "Close [10]"),
                ("Master trip", "PTRC1.Tr.general", "ACT / BOOLEAN", "false"),
                ("Close interlock release", "CILO1.EnaCls.stVal", "SPS / BOOLEAN", "true"),
                ("Control authority", "CSWI1.Loc.stVal", "ENS / INT32", "Remote")),
            DemoGooseSpec("gcb-linediff", "E02LDIF1", "0x1101", "01:0C:CD:01:01:01", 110, "gcbLineProtection", "dsLineProtection",
                ("Line differential pickup", "PDIF1.Str.general", "ACD / BOOLEAN", "false"),
                ("Line differential trip", "PDIF1.Op.general", "ACT / BOOLEAN", "false"),
                ("Teleprotection receive", "PSCH1.Op.general", "ACT / BOOLEAN", "false"),
                ("Breaker position", "XCBR1.Pos.stVal", "DPC / Dbpos", "Close [10]")),
            DemoGooseSpec("gcb-trafodiff", "E03TDIF1", "0x1201", "01:0C:CD:01:02:01", 120, "gcbTransformerTrip", "dsTransformerTrip",
                ("Transformer differential pickup", "PDIF1.Str.general", "ACD / BOOLEAN", "false"),
                ("Transformer differential trip", "PDIF1.Op.general", "ACT / BOOLEAN", "false"),
                ("Inrush restraint", "PHAR1.Str.general", "ACD / BOOLEAN", "false"),
                ("HV breaker position", "XCBR1.Pos.stVal", "DPC / Dbpos", "Close [10]")),
            DemoGooseSpec("gcb-busbar", "E04BDIF1", "0x1301", "01:0C:CD:01:03:01", 130, "gcbBusbarTrip", "dsBusbarTrip",
                ("Busbar differential pickup", "PDIF1.Str.general", "ACD / BOOLEAN", "false"),
                ("Busbar zone 1 trip", "PDIF1.Op.general", "ACT / BOOLEAN", "false"),
                ("Breaker-failure initiate", "RBRF1.OpEx.general", "ACT / BOOLEAN", "false"),
                ("Zone healthy", "GGIO1.Ind1.stVal", "SPS / BOOLEAN", "true")),
            DemoGooseSpec("gcb-coupler", "E06BCU3", "0x1401", "01:0C:CD:01:04:01", 140, "gcbCouplerInterlock", "dsCouplerInterlock",
                ("Coupler position", "XCBR1.Pos.stVal", "DPC / Dbpos", "Open [01]"),
                ("Synchrocheck release", "RSYN1.Rel.stVal", "SPS / BOOLEAN", "true"),
                ("Bus 1 voltage healthy", "GGIO1.Ind2.stVal", "SPS / BOOLEAN", "true"),
                ("Bus 2 voltage healthy", "GGIO1.Ind3.stVal", "SPS / BOOLEAN", "true")),
            DemoGooseSpec("gcb-feeder", "E06OCR2", "0x1501", "01:0C:CD:01:05:01", 150, "gcbFeederProtection", "dsFeederProtection",
                ("Overcurrent pickup", "PTOC1.Str.general", "ACD / BOOLEAN", "false"),
                ("Earth-fault pickup", "PTOC3.Str.general", "ACD / BOOLEAN", "false"),
                ("Master trip", "PTRC1.Tr.general", "ACT / BOOLEAN", "false"),
                ("Breaker position", "XCBR1.Pos.stVal", "DPC / Dbpos", "Close [10]"))
        };

        var baseTime = DateTimeOffset.Now.AddMinutes(-4);
        for (var index = 0; index < streamSpecs.Length; index++)
        {
            var state = new DemoGooseStreamState(streamSpecs[index], stateNumber: 12 + index * 3, sequenceNumber: 510 + index * 71, packetCount: 8200 + index * 1260);
            var row = new GooseStreamRow { StreamKey = state.Spec.StreamKey };
            state.Row = row;
            row.Apply(BuildDemoGooseSnapshot(state, changedIndex: -1, diagnostics: string.Empty, baseTime.AddMilliseconds(index * _demoRandom.Next(3, 19))));
            GooseStreams.Add(row);
            _gooseStreamIndex[row.StreamKey] = row;
            _demoGooseStates.Add(state);
        }

        var timelineCursor = baseTime;
        for (var index = 0; index < 30; index++)
        {
            var state = _demoGooseStates[index % _demoGooseStates.Count];
            var delta = index == 0 ? TimeSpan.Zero : NextGooseTimelineDelta(index);
            timelineCursor = timelineCursor.Add(delta);
            state.LastTimelineTimestamp = timelineCursor;
            var leaf = state.Leaves[index % state.Leaves.Count];
            GooseEvents.Add(new GooseEventRow
            {
                StreamKey = state.Spec.StreamKey,
                Timestamp = timelineCursor,
                DeltaText = index < _demoGooseStates.Count ? "-" : FormatGooseDelta(delta),
                EventText = index % 11 == 0 ? "Warning" : "State change",
                EventTone = index % 11 == 0 ? "Warning" : "Change",
                Publisher = state.Spec.IedName,
                StateSequenceText = $"{state.StateNumber + index / _demoGooseStates.Count} / 0",
                Summary = index % 11 == 0
                    ? "TAL supervision recovered after one delayed retransmission"
                    : $"{leaf.Name}: {ToggleDemoValue(leaf.Value)}"
            });
        }

        SelectedGooseStream = GooseStreams.FirstOrDefault();
        SelectedGooseEvent = GooseEvents.LastOrDefault();
        GooseCapturedFrames = _demoGooseStates.Sum(state => state.PacketCount) + 412;
        GooseFrames = GooseCapturedFrames;
        GooseOtherFrames = 0;
        Raise(nameof(GooseCounterText));
        Raise(nameof(GooseNoStreamsVisibility));
        Raise(nameof(GooseNoLeafValuesVisibility));
        RaiseGoosePresentationState();
    }

    private TimeSpan NextGooseTimelineDelta(int index)
    {
        var milliseconds = index % 7 switch
        {
            0 => _demoRandom.Next(1450, 6200),
            1 => _demoRandom.Next(2, 9),
            2 => _demoRandom.Next(9, 35),
            3 => _demoRandom.Next(35, 140),
            4 => _demoRandom.Next(140, 650),
            5 => _demoRandom.Next(650, 1900),
            _ => _demoRandom.Next(1900, 4800)
        };
        return TimeSpan.FromMilliseconds(milliseconds + _demoRandom.NextDouble());
    }

    private void BuildDemoDiagnostics()
    {
        var now = DateTime.Now;
        var logs = new List<DiagnosticEntry>
        {
            new() { Time = now.AddMinutes(-8), Level = "INFO", Source = "System", Message = "Connection health monitor started for 10 IEC 61850 sessions." },
            new() { Time = now.AddMinutes(-7.8), Level = "INFO", Source = "MMS", Message = "10 independent IEC 61850 associations established on TCP/102." },
            new() { Time = now.AddMinutes(-7.6), Level = "INFO", Source = "Discovery", Message = "IEDName, LD/LN/DO/DA, DataSets, RCBs and GSEControl models resolved for all connected IEDs." },
            new() { Time = now.AddMinutes(-7.4), Level = "INFO", Source = "Reporting", Message = "10 dynamic BRCB sessions enabled; dchg, qchg, dupd and integrity reasons verified." },
            new() { Time = now.AddMinutes(-7.2), Level = "INFO", Source = "GOOSE", Message = "6 station-bus publishers bound to ordered DataSet leaves on VLANs 100-150." },
            new() { Time = now.AddMinutes(-6.8), Level = "INFO", Source = "Quality", Message = "All monitored process values currently report quality Good with valid source timestamps." },
            new() { Time = now.AddMinutes(-4.1), Level = "WARN", Source = "GOOSE", Message = "One delayed retransmission exceeded the expected interval; TAL supervision recovered without state loss." },
            new() { Time = now.AddMinutes(-3.9), Level = "INFO", Source = "GOOSE", Message = "Publisher sequence returned to normal; stNum and sqNum continuity verified." },
            new() { Time = now.AddMinutes(-2.5), Level = "INFO", Source = "Control", Message = "CTRL/CSWI1.Pos DPC controls resolved with SBO Enhanced and live status feedback." },
            new() { Time = now.AddMinutes(-1.2), Level = "INFO", Source = "Reporting", Message = "Periodic integrity report completed for E03TDIF1; 12 values confirmed Good." }
        };
        Logs.AddRange(logs);
    }
}
