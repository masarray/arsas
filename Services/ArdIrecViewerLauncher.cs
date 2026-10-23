namespace ArIED61850Tester.Services;

/// <summary>
/// Compatibility facade retained only for existing CFG-resolution callers/tests.
/// External ArdIrec process launching was intentionally removed: ARSAS COMTRADE
/// records are opened exclusively through ArdIrecNativeBridge in-process.
/// </summary>
internal static class ArdIrecViewerLauncher
{
    public static bool TryResolveComtradeCfg(
        string localDirectory,
        string recordBaseName,
        out string cfgPath,
        out string error)
        => ComtradeRecordResolver.TryResolveCfg(
            localDirectory,
            recordBaseName,
            out cfgPath,
            out error);
}
