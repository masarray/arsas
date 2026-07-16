using System.Globalization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private GooseStreamSnapshot BuildDemoGooseSnapshot(DemoGooseStreamState state, int changedIndex, string diagnostics, DateTimeOffset timestamp)
    {
        var logicalDevice = state.Spec.IedName.Contains("BCU", StringComparison.OrdinalIgnoreCase) ? "CTRL" : "PROT";
        var leaves = state.Leaves.Select((leaf, index) => new GooseLeafValueSnapshot(
            index + 1,
            index,
            leaf.Name,
            $"{state.Spec.IedName}{logicalDevice}/{leaf.Path}",
            leaf.Path.Contains(".mag.", StringComparison.OrdinalIgnoreCase) ? "MX" : "ST",
            leaf.TypeText.Split('/')[0].Trim(),
            leaf.TypeText.Contains('/') ? leaf.TypeText.Split('/')[1].Trim() : string.Empty,
            leaf.Value,
            index == changedIndex ? leaf.PreviousValue : leaf.Value,
            index == changedIndex,
            "SCL / live model")).ToArray();

        return new GooseStreamSnapshot(
            state.Spec.StreamKey,
            state.Spec.AppId,
            $"{state.Spec.IedName}{logicalDevice}/LLN0$GO${state.Spec.ControlBlock}",
            $"{state.Spec.IedName}_{state.Spec.ControlBlock}",
            $"{state.Spec.IedName}{logicalDevice}/LLN0$DataSet${state.Spec.DataSet}",
            state.Spec.SourceMac,
            state.Spec.DestinationMac,
            $"VID {state.Spec.Vlan} / PCP 4",
            state.StateNumber.ToString(CultureInfo.InvariantCulture),
            state.SequenceNumber.ToString(CultureInfo.InvariantCulture),
            changedIndex >= 0 ? "StateChange" : "Retransmission",
            "2000 ms",
            "1",
            state.Spec.IedName,
            "SCL / live model",
            diagnostics,
            timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            state.PacketCount,
            changedIndex >= 0 ? 1 : 0,
            false,
            false,
            leaves);
    }

    private static DemoGooseStreamSpec DemoGooseSpec(
        string streamKey,
        string iedName,
        string appId,
        string destinationMac,
        int vlan,
        string controlBlock,
        string dataSet,
        params (string Name, string Path, string TypeText, string InitialValue)[] leaves)
    {
        var lastOctet = (Math.Abs((long)iedName.GetHashCode(StringComparison.Ordinal)) % 180) + 20;
        return new DemoGooseStreamSpec(
            streamKey,
            iedName,
            appId,
            $"02:00:5E:61:85:{lastOctet:X2}",
            destinationMac,
            vlan,
            controlBlock,
            dataSet,
            leaves.Select(leaf => new DemoGooseLeafState(leaf.Name, leaf.Path, leaf.TypeText, leaf.InitialValue)).ToArray());
    }
}
