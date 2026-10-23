using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ArIED61850Tester.Protocol.Asn1;
using ArIED61850Tester.Protocol.Diagnostics;

namespace ArIED61850Tester.Protocol.Mms;

public sealed class MmsVariableAccessAttributesResult
{
    public bool IsSuccess { get; init; }
    public IReadOnlyList<string> ComponentPaths { get; init; } = Array.Empty<string>();
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
}

public static class MmsVariableAccessAttributesResponseDecoder
{
    public static MmsVariableAccessAttributesResult Decode(byte[] presentationPayload, int expectedInvokeId)
    {
        var hex = HexDump.ToCompactString(presentationPayload);
        try
        {
            var mms = StripPresentationPrefix(presentationPayload);
            if (mms.Length == 0)
                return Fail("Empty MMS GetVariableAccessAttributes response payload.", hex);
            if (mms[0] == 0xA2)
                return Fail($"MMS Confirmed-Error PDU during GetVariableAccessAttributes: {HexDump.ToCompactString(mms)}", hex);
            if (mms[0] == 0xA3 || mms[0] == 0xA4)
                return Fail($"MMS Reject/Abort PDU during GetVariableAccessAttributes: {HexDump.ToCompactString(mms)}", hex);
            if (mms[0] != 0xA1)
                return Fail($"Expected MMS Confirmed-Response PDU [1] (0xA1), received 0x{mms[0]:X2}.", hex);

            var outer = new BerReader(mms).ReadTlv();
            var reader = new BerReader(outer.Value);
            if (reader.EndOfBuffer) return Fail("MMS Confirmed-Response PDU is empty.", hex);

            var invoke = reader.ReadTlv();
            if (invoke.Tag != 0x02)
                return Fail($"MMS GetVariableAccessAttributes response did not start with invokeID. First inner tag=0x{invoke.Tag:X2}.", hex);
            var actualInvoke = DecodeUnsigned(invoke.Value.Span);
            if (actualInvoke != expectedInvokeId)
                return Fail($"MMS GetVariableAccessAttributes invokeID mismatch. Expected {expectedInvokeId}, received {actualInvoke}.", hex);

            if (reader.EndOfBuffer) return Fail("MMS GetVariableAccessAttributes response has no service response node.", hex);
            var service = reader.ReadTlv();
            if (service.Tag != 0xA6)
                return Fail($"Expected MMS GetVariableAccessAttributes service response [6] (0xA6), received 0x{service.Tag:X2}.", hex);

            var typeSpecs = new List<BerTlv>();
            CollectLikelyTypeSpecifications(service, typeSpecs);
            var leaves = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var spec in typeSpecs)
            {
                foreach (var path in ExtractComponentPaths(spec, string.Empty, 0))
                {
                    var normalized = NormalizePath(path);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        leaves.Add(normalized);
                }
            }

            // If no structured path was decoded, expose immediate component names. This still lets
            // the caller run a second attribute pass for FC/DO objects.
            if (leaves.Count == 0)
            {
                foreach (var name in ExtractVisibleStrings(service.Value).Where(LooksLikeComponentName))
                    leaves.Add(name);
            }

            return new MmsVariableAccessAttributesResult
            {
                IsSuccess = true,
                ComponentPaths = leaves.ToList(),
                Message = $"MMS GetVariableAccessAttributes decoded {leaves.Count} component path(s).",
                ResponseHexPreview = hex
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return Fail($"MMS GetVariableAccessAttributes response decode failed: {ex.GetType().Name}: {ex.Message}", hex);
        }
    }

    private static void CollectLikelyTypeSpecifications(BerTlv service, ICollection<BerTlv> output)
    {
        var reader = new BerReader(service.Value);
        while (!reader.EndOfBuffer)
        {
            var field = reader.ReadTlv();
            // Skip mmsDeletable BOOLEAN and optional access-control metadata. TypeSpecification is a
            // CHOICE and therefore appears as one of the context-specific type tags directly.
            if (IsTypeSpecificationTag(field.Tag))
                output.Add(field);
            else if (field.Tag is 0x30 or 0xA0 or 0xA1 or 0xA2 or 0xA3)
                CollectNestedTypeSpecifications(field, output, 0);
        }
    }

    private static void CollectNestedTypeSpecifications(BerTlv root, ICollection<BerTlv> output, int depth)
    {
        if (depth > 8 || root.Length == 0) return;
        try
        {
            var reader = new BerReader(root.Value);
            while (!reader.EndOfBuffer)
            {
                var child = reader.ReadTlv();
                if (IsTypeSpecificationTag(child.Tag))
                    output.Add(child);
                else if (IsConstructed(child.Tag))
                    CollectNestedTypeSpecifications(child, output, depth + 1);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            // Non-constructed value that happens to have a constructed-looking tag; ignore.
        }
    }

    private static IEnumerable<string> ExtractComponentPaths(BerTlv typeSpec, string prefix, int depth)
    {
        if (depth > 16) yield break;

        // structure [2] IMPLICIT SEQUENCE OF SEQUENCE { componentName Identifier OPTIONAL, componentType TypeSpecification }
        if (typeSpec.Tag == 0xA2 || typeSpec.Tag == 0x82)
        {
            foreach (var component in ReadStructureComponents(typeSpec.Value))
            {
                var componentName = component.Name;
                var componentPrefix = string.IsNullOrWhiteSpace(componentName)
                    ? prefix
                    : string.IsNullOrWhiteSpace(prefix) ? componentName : $"{prefix}${componentName}";

                var emittedChild = false;
                foreach (var child in ExtractComponentPaths(component.TypeSpec, componentPrefix, depth + 1))
                {
                    emittedChild = true;
                    yield return child;
                }

                if (!emittedChild && !string.IsNullOrWhiteSpace(componentPrefix))
                    yield return componentPrefix;
            }
            yield break;
        }

        // array [1] wraps an element type. Keep the same prefix because IEC 61850 scalar arrays are
        // usually not SCADA-published as separate indexed points in this tool.
        if (typeSpec.Tag == 0xA1 || typeSpec.Tag == 0x81)
        {
            foreach (var childSpec in FindNestedTypeSpecs(typeSpec.Value))
                foreach (var child in ExtractComponentPaths(childSpec, prefix, depth + 1))
                    yield return child;
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(prefix) && IsScalarTypeSpecificationTag(typeSpec.Tag))
            yield return prefix;
    }

    private readonly record struct ComponentSpec(string Name, BerTlv TypeSpec);

    private static IEnumerable<ComponentSpec> ReadStructureComponents(ReadOnlyMemory<byte> value)
    {
        var reader = new BerReader(value);
        while (!reader.EndOfBuffer)
        {
            BerTlv component;
            try
            {
                component = reader.ReadTlv();
            }
            catch
            {
                break;
            }

            if (component.Tag == 0x30)
            {
                var name = string.Empty;
                BerTlv? typeSpec = null;
                var componentReader = new BerReader(component.Value);
                while (!componentReader.EndOfBuffer)
                {
                    var field = componentReader.ReadTlv();
                    if (IsIdentifierTag(field.Tag) || field.Tag == 0x80)
                    {
                        var decoded = DecodeString(field.Value.Span);
                        if (!string.IsNullOrWhiteSpace(decoded)) name = decoded;
                        continue;
                    }

                    if (IsTypeSpecificationTag(field.Tag))
                    {
                        typeSpec = field;
                        break;
                    }

                    if (IsConstructed(field.Tag))
                    {
                        var nested = FindNestedTypeSpecs(field.Value).FirstOrDefault();
                        if (nested.Tag != 0) typeSpec = nested;
                    }
                }

                if (typeSpec.HasValue)
                    yield return new ComponentSpec(name, typeSpec.Value);
            }
            else if (IsTypeSpecificationTag(component.Tag))
            {
                yield return new ComponentSpec(string.Empty, component);
            }
        }
    }

    private static IEnumerable<BerTlv> FindNestedTypeSpecs(ReadOnlyMemory<byte> value)
    {
        var reader = new BerReader(value);
        while (!reader.EndOfBuffer)
        {
            var field = reader.ReadTlv();
            if (IsTypeSpecificationTag(field.Tag))
            {
                yield return field;
            }
            else if (IsConstructed(field.Tag))
            {
                foreach (var nested in FindNestedTypeSpecs(field.Value))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<string> ExtractVisibleStrings(ReadOnlyMemory<byte> value)
    {
        var reader = new BerReader(value);
        while (!reader.EndOfBuffer)
        {
            var field = reader.ReadTlv();
            if (IsIdentifierTag(field.Tag) || field.Tag == 0x80 || field.Tag == 0x81 || field.Tag == 0x82)
            {
                var text = DecodeString(field.Value.Span);
                if (!string.IsNullOrWhiteSpace(text)) yield return text;
            }
            if (IsConstructed(field.Tag))
            {
                foreach (var child in ExtractVisibleStrings(field.Value))
                    yield return child;
            }
        }
    }

    private static bool LooksLikeComponentName(string text)
        => text.Length <= 64 && text.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    private static bool IsTypeSpecificationTag(byte tag)
        => tag is 0xA1 or 0x81 or 0xA2 or 0x82 or 0x83 or 0x84 or 0x85 or 0x86 or 0x87 or 0x89 or 0x8A or 0x8C or 0x8D or 0x8F or 0x90 or 0x91;

    private static bool IsScalarTypeSpecificationTag(byte tag)
        => tag is 0x83 or 0x84 or 0x85 or 0x86 or 0x87 or 0x89 or 0x8A or 0x8C or 0x8D or 0x8F or 0x90 or 0x91;

    private static bool IsIdentifierTag(byte tag)
        => tag is 0x1A or 0x16 or 0x0C or 0x19 or 0x1B;

    private static bool IsConstructed(byte tag)
        => (tag & 0x20) == 0x20 || tag is 0x30;

    private static string NormalizePath(string path)
        => (path ?? string.Empty).Trim().Replace('.', '$').Trim('$');

    private static string DecodeString(ReadOnlySpan<byte> span)
    {
        if (span.Length == 0) return string.Empty;
        try
        {
            return Encoding.ASCII.GetString(span).Trim('\0', ' ', '\r', '\n', '\t');
        }
        catch
        {
            return string.Empty;
        }
    }

    private static uint DecodeUnsigned(ReadOnlySpan<byte> bytes)
    {
        uint value = 0;
        foreach (var b in bytes) value = (value << 8) | b;
        return value;
    }

    private static MmsVariableAccessAttributesResult Fail(string message, string hex) => new()
    {
        IsSuccess = false,
        ComponentPaths = Array.Empty<string>(),
        Message = message,
        ResponseHexPreview = hex
    };

    private static byte[] StripPresentationPrefix(byte[] payload)
    {
        if (payload.Length == 0) return payload;
        if (payload.Length > 5 && payload[0] == 0x01 && payload[1] == 0x00 && payload[2] == 0x01 && payload[3] == 0x00 && payload[4] == 0x61)
        {
            if (TryExtractMmsFromFullyEncodedData(payload.AsMemory(4), out var mms)) return mms;
        }
        if (payload.Length > 3 && payload[0] == 0x01 && payload[1] == 0x00 && payload[2] == 0x61)
        {
            if (TryExtractMmsFromFullyEncodedData(payload.AsMemory(2), out var mms)) return mms;
        }
        if (payload[0] == 0x61 && TryExtractMmsFromFullyEncodedData(payload, out var directMms))
            return directMms;
        if (payload.Length > 2 && payload[0] == 0x01 && payload[1] == 0x00 && (payload[2] & 0xE0) == 0xA0)
            return payload.Skip(2).ToArray();
        return payload;
    }

    private static bool TryExtractMmsFromFullyEncodedData(ReadOnlyMemory<byte> payload, out byte[] mms)
    {
        mms = Array.Empty<byte>();
        try
        {
            var outer = new BerReader(payload).ReadTlv();
            if (outer.Tag != 0x61) return false;
            var listReader = new BerReader(outer.Value);
            if (listReader.EndOfBuffer) return false;
            var pdvList = listReader.ReadTlv();
            if (pdvList.Tag != 0x30) return false;
            var pdvReader = new BerReader(pdvList.Value);
            while (!pdvReader.EndOfBuffer)
            {
                var item = pdvReader.ReadTlv();
                if (item.Tag == 0xA0)
                {
                    mms = item.Value.ToArray();
                    return mms.Length > 0;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException)
        {
            return false;
        }
        return false;
    }
}
