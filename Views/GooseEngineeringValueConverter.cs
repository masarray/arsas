using System.Globalization;
using System.Windows.Data;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Views;

public sealed class GooseEngineeringValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => GooseEngineeringValueFormatter.Format(value?.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
