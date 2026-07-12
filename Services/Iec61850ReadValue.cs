using System.Globalization;

namespace ArIED61850Tester.Services;

public sealed class Iec61850ReadValue
{
    public object? Value { get; init; }
    public string DisplayValue { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public string DeviceTimestamp { get; init; } = string.Empty;
    public string SourceReference { get; init; } = string.Empty;
    public string ReadReference { get; init; } = string.Empty;
    public string Projection { get; init; } = string.Empty;

    public bool HasQuality => !string.IsNullOrWhiteSpace(Quality) && Quality != "-";
    public bool HasDeviceTimestamp => !string.IsNullOrWhiteSpace(DeviceTimestamp) && DeviceTimestamp != "-";

    public override string ToString()
    {
        if (!string.IsNullOrWhiteSpace(DisplayValue))
            return DisplayValue;

        return Value switch
        {
            null => "-",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => Value.ToString() ?? "-"
        };
    }

    public static object? Unwrap(object? value)
        => value is Iec61850ReadValue read ? read.Value : value;
}
