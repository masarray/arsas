using System.Globalization;

namespace ArIED61850Tester.Models;

public sealed partial class GooseStreamRow
{
    public string DisplayName => FirstReadable(ModelIedName, GoId, ShortReference(GoCbRef), $"GOOSE {AppIdText}");

    public string DisplaySecondary
    {
        get
        {
            var primary = DisplayName;
            foreach (var candidate in new[] { GoId, ShortReference(GoCbRef), DataSetShortName })
            {
                if (!string.IsNullOrWhiteSpace(candidate) && !candidate.Equals(primary, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return "GOOSE publisher";
        }
    }

    public string ModelIedDisplay => string.IsNullOrWhiteSpace(ModelIedName) ? "Not resolved" : ModelIedName;
    public string GooseIdDisplay => string.IsNullOrWhiteSpace(GoId) ? "Not provided" : GoId;
    public string DataSetShortName => string.IsNullOrWhiteSpace(DataSetReference) ? "Not provided" : ShortReference(DataSetReference);
    public string GoCbRefShortName => string.IsNullOrWhiteSpace(GoCbRef) ? "Not provided" : ShortReference(GoCbRef);
    public string StateSequenceText => $"{StateNumberText} / {SequenceNumberText}";
    public string StateSequenceCompactText => $"st {StateNumberText} • sq {SequenceNumberText}";
    public string ModelStateText => BindingSource.Equals("Unbound", StringComparison.OrdinalIgnoreCase)
        ? "Raw / unbound"
        : BindingSource;

    private void RaisePresentationProperties()
    {
        Raise(nameof(DisplayName));
        Raise(nameof(DisplaySecondary));
        Raise(nameof(ModelIedDisplay));
        Raise(nameof(GooseIdDisplay));
        Raise(nameof(DataSetShortName));
        Raise(nameof(GoCbRefShortName));
        Raise(nameof(StateSequenceText));
        Raise(nameof(StateSequenceCompactText));
        Raise(nameof(ModelStateText));
    }

    private static string FirstReadable(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "GOOSE publisher";

    private static string ShortReference(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var slash = text.LastIndexOf('/');
        if (slash >= 0 && slash < text.Length - 1)
            text = text[(slash + 1)..];
        return text.Replace('$', '.');
    }
}

public sealed class GooseEventRow
{
    public required string StreamKey { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string DeltaText { get; init; }
    public required string EventText { get; init; }
    public required string EventTone { get; init; }
    public required string Publisher { get; init; }
    public required string StateSequenceText { get; init; }
    public required string Summary { get; init; }

    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
