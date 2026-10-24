using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ArIED61850Tester.Views;

/// <summary>
/// Stable DataGrid row, updated from an immutable engineering measurement.
/// Rows and row containers are not replaced while samples stream. The
/// measurement values remain calculated in SmvEngineeringDisplay; this class
/// changes only their presentation notification.
/// </summary>
public sealed class SmvMeasurementLiveRow : INotifyPropertyChanged
{
    private SmvDisplayMeasurement _current;

    public SmvMeasurementLiveRow(SmvDisplayMeasurement current) => _current = current;

    public int ChannelIndex => _current.ChannelIndex;
    public string DisplayName => _current.DisplayName;
    public string Unit => _current.Unit;
    public string Label => _current.Label;
    public string RmsText => _current.RmsText;
    public string FundamentalRmsText => _current.FundamentalRmsText;
    public string RmsDetail => _current.RmsDetail;
    public string AngleText => _current.AngleText;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(SmvDisplayMeasurement next)
    {
        if (next.ChannelIndex != _current.ChannelIndex)
            throw new InvalidOperationException("A live SMV row cannot change its wire channel identity.");
        if (Equals(_current, next))
            return;

        var prev = _current;
        _current = next;
        if (prev.DisplayName != next.DisplayName) Notify(nameof(DisplayName));
        if (prev.Unit != next.Unit) Notify(nameof(Unit));
        if (prev.Label != next.Label) Notify(nameof(Label));
        if (prev.RmsText != next.RmsText) Notify(nameof(RmsText));
        if (prev.FundamentalRmsText != next.FundamentalRmsText) Notify(nameof(FundamentalRmsText));
        if (prev.RmsDetail != next.RmsDetail) Notify(nameof(RmsDetail));
        if (prev.AngleText != next.AngleText) Notify(nameof(AngleText));
    }

    private void Notify([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
