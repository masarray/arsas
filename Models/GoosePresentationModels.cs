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
    public string ApplicationIdDecimal
    {
        get
        {
            var raw = AppIdText.Trim();
            return raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                   ushort.TryParse(raw.AsSpan(2), NumberStyles.HexNumber,
                       CultureInfo.InvariantCulture, out var value)
                ? value.ToString(CultureInfo.InvariantCulture) : raw;
        }
    }
    public string SimulationTestText => Test ? "True" : "False";
    public string DataSetEntryCountText => WireDataSetEntryCount.ToString(CultureInfo.InvariantCulture);
    public string DataSetShortName => string.IsNullOrWhiteSpace(DataSetReference) ? "Not provided" : ShortReference(DataSetReference);
    public string GoCbRefShortName => string.IsNullOrWhiteSpace(GoCbRef) ? "Not provided" : ShortReference(GoCbRef);
    public string StateSequenceText => $"{StateNumberText} / {SequenceNumberText}";
    public string StateSequenceCompactText => $"st {StateNumberText} • sq {SequenceNumberText}";
    public string ModelStateText => BindingSource.Equals("Unbound", StringComparison.OrdinalIgnoreCase)
        ? "Signal names unavailable · open matching publisher SCL/CID"
        : BindingSource;

    private void RaisePresentationProperties()
    {
        Raise(nameof(DisplayName));
        Raise(nameof(DisplaySecondary));
        Raise(nameof(ModelIedDisplay));
        Raise(nameof(ApplicationIdDecimal));
        Raise(nameof(SimulationTestText));
        Raise(nameof(DataSetEntryCountText));
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

public sealed class GooseEventRow : ObservableObject
{
    private bool _isRecent = true;

    public required string StreamKey { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string DeltaText { get; init; }
    public required string EventText { get; init; }
    public required string EventTone { get; init; }
    public required string Publisher { get; init; }
    public required string StateSequenceText { get; init; }
    public required string Summary { get; init; }
    // Real wire events populate all six-column fields plus an immutable frame.
    // Simulated demo events may omit the frame; they keep their explicit values.
    public string SourceMac { get; init; } = "—";
    public string DestinationMac { get; init; } = "—";
    public string DataSetName { get; init; } = "—";
    public string RelativeTime { get; init; } = "—";
    public string IedName { get; init; } = "Unresolved";
    public string GooseId { get; init; } = "Not provided";
    public GooseStreamSnapshot? Snapshot { get; init; }

    public bool IsRecent { get => _isRecent; private set => Set(ref _isRecent, value); }
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public bool ExpireHighlight(DateTimeOffset nowUtc)
    {
        if (!IsRecent || nowUtc - Timestamp < TimeSpan.FromSeconds(3))
            return false;
        IsRecent = false;
        return true;
    }
}
