using System.Windows.Media;
using ArIED61850Tester.Controls;

namespace ARSAS.Tests;

public sealed class ComtradeTrackOrderingRegressionTests
{
    [Fact]
    public void StableAnalogThenDigital_MovesLateCheckedAnalogAboveDigitalTracks()
    {
        var digitalA = Track("trip", isDigital: true);
        var analogA = Track("IA", isDigital: false);
        var digitalB = Track("pickup", isDigital: true);
        var analogB = Track("IB", isDigital: false);

        var ordered = ComtradeDisturbanceViewP1D3ShellAware.StableAnalogThenDigital(
            new[] { digitalA, analogA, digitalB, analogB });

        Assert.Equal(new[] { "IA", "IB", "trip", "pickup" }, ordered.Select(track => track.Title));
    }

    [Fact]
    public void StableAnalogThenDigital_PreservesRelativeOrderInsideEachCategory()
    {
        var input = new[]
        {
            Track("D2", isDigital: true),
            Track("A3", isDigital: false),
            Track("A1", isDigital: false),
            Track("D1", isDigital: true),
            Track("A2", isDigital: false)
        };

        var ordered = ComtradeDisturbanceViewP1D3ShellAware.StableAnalogThenDigital(input);

        Assert.Equal(new[] { "A3", "A1", "A2", "D2", "D1" }, ordered.Select(track => track.Title));
    }

    [Fact]
    public void StableAnalogThenDigital_ReturnsSameCollectionWhenAlreadyCanonical()
    {
        IReadOnlyList<ComtradeDisturbanceTrack> input = new[]
        {
            Track("VA", isDigital: false),
            Track("IA", isDigital: false),
            Track("trip", isDigital: true)
        };

        var ordered = ComtradeDisturbanceViewP1D3ShellAware.StableAnalogThenDigital(input);

        Assert.Same(input, ordered);
    }

    private static ComtradeDisturbanceTrack Track(string title, bool isDigital)
        => new(
            title,
            string.Empty,
            isDigital ? string.Empty : "A",
            isDigital,
            isDigital ? null : new[] { 0.0, 1.0 },
            isDigital ? new byte[] { 0, 1 } : null,
            new uint[] { 0, 1000 },
            Colors.SteelBlue);
}
