using System.Text.RegularExpressions;

namespace ArIED61850Tester.Services;

/// <summary>
/// Immutable identity captured when an SV snapshot starts. The completed result may only be
/// attached to the same operator selection that created the request.
/// </summary>
public sealed record SmvSnapshotSelectionIdentity(
    string ControlReference,
    string StreamId,
    string DataSetReference,
    string AppId,
    string DestinationMac)
{
    public static SmvSnapshotSelectionIdentity Create(
        string? controlReference,
        string? streamId,
        string? dataSetReference,
        string? appId,
        string? destinationMac)
        => new(
            NormalizeReference(controlReference),
            NormalizeIdentity(streamId),
            NormalizeReference(dataSetReference),
            NormalizeAppId(appId),
            NormalizeMac(destinationMac));

    public bool Matches(
        string? controlReference,
        string? streamId,
        string? dataSetReference,
        string? appId,
        string? destinationMac)
        => this == Create(controlReference, streamId, dataSetReference, appId, destinationMac);

    private static string NormalizeReference(string? value)
        => NormalizeText(value).Replace('$', '.').ToUpperInvariant();

    private static string NormalizeIdentity(string? value)
        => NormalizeText(value).ToUpperInvariant();

    private static string NormalizeText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text == "-" ? string.Empty : text;
    }

    private static string NormalizeAppId(string? value)
    {
        var text = NormalizeText(value);
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        return text.ToUpperInvariant();
    }

    private static string NormalizeMac(string? value)
        => Regex.Replace(value ?? string.Empty, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();
}
