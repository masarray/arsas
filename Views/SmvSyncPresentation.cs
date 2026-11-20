using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Views;

/// <summary>
/// IEC 61850-9-2 sample publisher's reported smpSynch state, NOT receiver-side
/// PTP/GPS verification. An absent/stale/invalid snapshot is always Unknown.
/// </summary>
public static class SmvSyncPresentation
{
    private static readonly Brush GlobalBg = Frozen("#E4F7ED");
    private static readonly Brush GlobalInk = Frozen("#147D49");
    private static readonly Brush LocalBg = Frozen("#E8F1FF");
    private static readonly Brush LocalInk = Frozen("#295DB8");
    private static readonly Brush NoneBg = Frozen("#FFF1E5");
    private static readonly Brush NoneInk = Frozen("#995218");
    private static readonly Brush UnknownBg = Frozen("#EEF2F7");
    private static readonly Brush UnknownInk = Frozen("#59687E");

    public static string Label(SmvSnapshotResult? snapshot) => snapshot switch
    {
        { IsCleanProof: true, SampleSynchronization: 2 } => "Global Sync",
        { IsCleanProof: true, SampleSynchronization: 1 } => "Local Sync",
        { IsCleanProof: true, SampleSynchronization: 0 } => "Sync None",
        _ => "Sync Unknown"
    };

    public static string Detail(SmvSnapshotResult? snapshot) => snapshot switch
    {
        { IsCleanProof: true, SampleSynchronization: <= 2 } =>
            $"Publisher-reported smpSynch={snapshot.SampleSynchronization} (IEC 61850 Ed.2 semantics). Does NOT verify PTP, GPS or grandmaster quality.",
        null => "No selected coherent SV snapshot; publisher synchronization not known.",
        _ => "smpSynch absent, outside 0–2 or sample continuity not qualified."
    };

    public static Brush Background(SmvSnapshotResult? snapshot) => Label(snapshot) switch
    {
        "Global Sync" => GlobalBg,
        "Local Sync" => LocalBg,
        "Sync None" => NoneBg,
        _ => UnknownBg
    };
    public static Brush Foreground(SmvSnapshotResult? snapshot) => Label(snapshot) switch
    {
        "Global Sync" => GlobalInk,
        "Local Sync" => LocalInk,
        "Sync None" => NoneInk,
        _ => UnknownInk
    };

    private static Brush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
