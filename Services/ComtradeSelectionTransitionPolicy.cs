namespace ArIED61850Tester.Services;

internal readonly record struct ComtradeSelectionNavigationSnapshot(
    bool IsValid,
    double ViewStartMilliseconds,
    double ViewEndMilliseconds,
    double? Cursor1Milliseconds,
    double? Cursor2Milliseconds,
    ulong SourceStartFrame,
    ulong SourceFrameCount)
{
    internal static ComtradeSelectionNavigationSnapshot Capture(
        double viewStartMilliseconds,
        double viewEndMilliseconds,
        double? cursor1Milliseconds,
        double? cursor2Milliseconds,
        ulong sourceStartFrame,
        ulong sourceFrameCount)
    {
        var validView = double.IsFinite(viewStartMilliseconds) &&
                        double.IsFinite(viewEndMilliseconds) &&
                        viewEndMilliseconds > viewStartMilliseconds &&
                        sourceFrameCount > 0;
        if (!validView)
            return default;

        return new ComtradeSelectionNavigationSnapshot(
            true,
            viewStartMilliseconds,
            viewEndMilliseconds,
            Finite(cursor1Milliseconds),
            Finite(cursor2Milliseconds),
            sourceStartFrame,
            sourceFrameCount);
    }

    internal bool MatchesSource(ulong sourceStartFrame, ulong sourceFrameCount)
        => IsValid && SourceStartFrame == sourceStartFrame && SourceFrameCount == sourceFrameCount;

    private static double? Finite(double? value)
        => value is { } finite && double.IsFinite(finite) ? finite : null;
}
