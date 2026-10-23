using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

internal sealed class IoFatBooleanValueBrushConverter : IValueConverter
{
    private static readonly Brush TrueBrush = Brush(229, 72, 77);
    private static readonly Brush FalseBrush = Brush(22, 166, 106);
    private static readonly Brush NeutralBrush = Brush(17, 24, 39);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString()?.Trim() ?? string.Empty;
        if (text.Equals("True", StringComparison.OrdinalIgnoreCase) || text == "1") return TrueBrush;
        if (text.Equals("False", StringComparison.OrdinalIgnoreCase) || text == "0") return FalseBrush;
        return NeutralBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    private static Brush Brush(byte r, byte g, byte b) { var value = new SolidColorBrush(Color.FromRgb(r, g, b)); value.Freeze(); return value; }
}

internal sealed class IoFatLiveBrushConverter : IValueConverter
{
    private static readonly Brush LiveBrush = Brush(22, 132, 90);
    private static readonly Brush MutedBrush = Brush(101, 117, 139);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? LiveBrush : MutedBrush;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    private static Brush Brush(byte r, byte g, byte b) { var value = new SolidColorBrush(Color.FromRgb(r, g, b)); value.Freeze(); return value; }
}

internal sealed class IoFatEvidenceBrushConverter : IValueConverter
{
    private static readonly Brush SuccessBrush = Brush(22, 132, 90);
    private static readonly Brush MutedBrush = Brush(138, 151, 169);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value == null ? MutedBrush : SuccessBrush;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    private static Brush Brush(byte r, byte g, byte b) { var result = new SolidColorBrush(Color.FromRgb(r, g, b)); result.Freeze(); return result; }
}

internal sealed class IoFatQualityBrushConverter : IValueConverter
{
    private static readonly Brush GoodBrush = Brush(22, 132, 90);
    private static readonly Brush BadBrush = Brush(197, 58, 69);
    private static readonly Brush MutedBrush = Brush(96, 112, 137);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString()?.Trim() ?? string.Empty;
        if (text.Equals("good", StringComparison.OrdinalIgnoreCase)) return GoodBrush;
        if (text.Contains("invalid", StringComparison.OrdinalIgnoreCase) || text.Contains("bad", StringComparison.OrdinalIgnoreCase)) return BadBrush;
        return MutedBrush;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    private static Brush Brush(byte r, byte g, byte b) { var result = new SolidColorBrush(Color.FromRgb(r, g, b)); result.Freeze(); return result; }
}

internal sealed class IoFatStateBrushConverter : IValueConverter
{
    private static readonly Brush PassBrush = Brush(22, 132, 90);
    private static readonly Brush ReviewBrush = Brush(154, 101, 0);
    private static readonly Brush FailBrush = Brush(197, 58, 69);
    private static readonly Brush MutedBrush = Brush(96, 112, 137);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        IoTestPointState.Passed => PassBrush,
        IoTestPointState.Review => ReviewBrush,
        IoTestPointState.Failed => FailBrush,
        _ => MutedBrush
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    private static Brush Brush(byte r, byte g, byte b) { var result = new SolidColorBrush(Color.FromRgb(r, g, b)); result.Freeze(); return result; }
}

internal sealed class IoFatResultTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        IoTestPointState.Passed => "✔ PASS",
        IoTestPointState.Review => "⚠ REVIEW",
        IoTestPointState.Failed => "✖ FAILED",
        _ => "—"
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
