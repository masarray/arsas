using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Stable identity contract for restoring persisted FAT progress onto a freshly imported plan.
/// IED ownership is technical-key + IP scoped; point progress is accepted only when the
/// evidence-critical configuration fingerprint still matches.
/// </summary>
public static class IoTestPerIedProgressIdentity
{
    public static string IedKey(IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(ied);
        return IedKey(ied.IedName, ied.IpAddress);
    }

    public static string IedKey(string? iedName, string? ipAddress)
        => $"{NormalizeIdentity(iedName)}|{NormalizeIp(ipAddress)}";

    public static string PointConfigurationFingerprint(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return HashCanonical(new[]
        {
            Pair("objectReference", point.ObjectReference),
            Pair("functionalConstraint", point.FunctionalConstraint, upper: true),
            Pair("expectedOnRaw", point.ExpectedOnRaw.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Pair("expectedOffRaw", point.ExpectedOffRaw.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Pair("dataType", point.DataType, upper: true),
            Pair("signalAddress", point.SignalAddress),
            Pair("dataSetName", point.DataSetName),
            Pair("logicalDevice", point.LogicalDevice),
            Pair("logicalNode", point.LogicalNode),
            Pair("dataObject", point.DataObject),
            Pair("dataAttribute", point.DataAttribute),
            Pair("cdc", point.Cdc, upper: true),
            Pair("sourceIecReference", point.SourceIecReference),
            Pair("reportDisplayReference", point.ReportDisplayReference),
            Pair("eventLogSearchReference", point.EventLogSearchReference),
            Pair("evidenceExpected", point.EvidenceExpected),
            Pair("signalKind", ((int)point.SignalKind).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Pair("captureMode", ((int)point.CaptureMode).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Pair("importReady", point.ImportReady ? "True" : "False")
        });
    }

    public static string SnapshotPointConfigurationFingerprint(JsonElement savedPoint)
        => HashCanonical(new[]
        {
            Pair("objectReference", RequiredString(savedPoint, "objectReference")),
            Pair("functionalConstraint", OptionalString(savedPoint, "functionalConstraint", string.Empty), upper: true),
            Pair("expectedOnRaw", OptionalInt(savedPoint, "expectedOnRaw", 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Pair("expectedOffRaw", OptionalInt(savedPoint, "expectedOffRaw", 0).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Pair("dataType", OptionalString(savedPoint, "dataType", "SDI"), upper: true),
            Pair("signalAddress", OptionalString(savedPoint, "signalAddress", string.Empty)),
            Pair("dataSetName", OptionalString(savedPoint, "dataSetName", string.Empty)),
            Pair("logicalDevice", OptionalString(savedPoint, "logicalDevice", string.Empty)),
            Pair("logicalNode", OptionalString(savedPoint, "logicalNode", string.Empty)),
            Pair("dataObject", OptionalString(savedPoint, "dataObject", string.Empty)),
            Pair("dataAttribute", OptionalString(savedPoint, "dataAttribute", string.Empty)),
            Pair("cdc", OptionalString(savedPoint, "cdc", string.Empty), upper: true),
            Pair("sourceIecReference", OptionalString(savedPoint, "sourceIecReference", string.Empty)),
            Pair("reportDisplayReference", OptionalString(savedPoint, "reportDisplayReference", string.Empty)),
            Pair("eventLogSearchReference", OptionalString(savedPoint, "eventLogSearchReference", string.Empty)),
            Pair("evidenceExpected", OptionalString(savedPoint, "evidenceExpected", string.Empty)),
            Pair("signalKind", ((int)OptionalEnum(savedPoint, "signalKind", FatSignalKind.Discrete)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Pair("captureMode", ((int)OptionalEnum(savedPoint, "captureMode", FatCaptureMode.AutomaticTransition)).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            Pair("importReady", OptionalBool(savedPoint, "importReady", true) ? "True" : "False")
        });

    public static bool PointConfigurationMatches(IoTestPointPlan currentPoint, JsonElement savedPoint)
        => PointConfigurationFingerprint(currentPoint)
            .Equals(SnapshotPointConfigurationFingerprint(savedPoint), StringComparison.OrdinalIgnoreCase);

    public static string IedConfigurationFingerprint(IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(ied);
        var pointEntries = ied.TestPoints
            .Select(point => $"{NormalizePointId(point.TestPointId)}={PointConfigurationFingerprint(point)}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return HashCanonical(pointEntries);
    }

    private static string NormalizePointId(string? value)
        => Normalize(value).ToUpperInvariant();

    private static string NormalizeIdentity(string? value)
        => Normalize(value).ToUpperInvariant();

    private static string NormalizeIp(string? value)
        => Normalize(value).ToLowerInvariant();

    private static string Pair(string name, string? value, bool upper = false)
    {
        var normalized = Normalize(value);
        if (upper)
            normalized = normalized.ToUpperInvariant();
        return $"{name}={normalized}";
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string HashCanonical(IEnumerable<string> values)
    {
        var canonical = string.Join("\n", values);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string RequiredString(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Snapshot property '{property}' is missing or invalid.");
        return value.GetString() ?? string.Empty;
    }

    private static string OptionalString(JsonElement parent, string property, string fallback)
        => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool OptionalBool(JsonElement parent, string property, bool fallback)
        => parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int OptionalInt(JsonElement parent, string property, int fallback)
        => parent.TryGetProperty(property, out var value) && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static TEnum OptionalEnum<TEnum>(JsonElement parent, string property, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (!parent.TryGetProperty(property, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && Enum.IsDefined(typeof(TEnum), number))
            return (TEnum)Enum.ToObject(typeof(TEnum), number);
        if (value.ValueKind == JsonValueKind.String && Enum.TryParse<TEnum>(value.GetString(), ignoreCase: true, out var parsed))
            return parsed;
        return fallback;
    }
}