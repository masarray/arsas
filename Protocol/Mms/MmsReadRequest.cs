using System;
using ArIED61850Tester.Protocol.Asn1;

namespace ArIED61850Tester.Protocol.Mms;

public static class MmsReadRequest
{
    /// <summary>
    /// Builds an MMS Confirmed-Read request for one IEC 61850 domain-specific named variable.
    ///
    /// Phase N7 correction:
    /// - The MMS Read-Request now follows the observed/standard MMS structure:
    ///   ConfirmedRequestPDU -> read [4] -> variableAccessSpecification [1] -> listOfVariable [0]
    ///   -> SEQUENCE OF VariableSpecification -> name [0] -> ObjectName.domain-specific [1].
    /// - The MMS PDU is wrapped in ISO Presentation P-DATA PDV-list:
    ///   01 00 01 00 61 ... 30 ... 02 01 03 A0 ... <MMS PDU>
    ///
    /// This remains a native implementation. The wrapper layout is derived from public protocol
    /// traces/documentation, not from third-party source code.
    /// </summary>
    public static byte[] BuildSingleVariableRead(int invokeId, MmsObjectReference reference, MmsReadPayloadProfile payloadProfile = MmsReadPayloadProfile.PresentationDataValues)
    {
        if (string.IsNullOrWhiteSpace(reference.Domain))
            throw new ArgumentException("MMS domain is empty. Use an SCL/IEC object reference such as LD0/LLN0.Mod.stVal.", nameof(reference));
        if (string.IsNullOrWhiteSpace(reference.Item))
            throw new ArgumentException("MMS item is empty.", nameof(reference));

        var includeSpecificationWithResult = payloadProfile == MmsReadPayloadProfile.PresentationDataValuesWithSpecificationResult;
        var mmsPdu = BuildConfirmedReadPdu(invokeId, reference, includeSpecificationWithResult);
        return WrapForPayloadProfile(mmsPdu, payloadProfile);
    }

    public static byte[] BuildConfirmedReadPdu(int invokeId, MmsObjectReference reference, bool includeSpecificationWithResult = false)
    {
        var domainId = VisibleString(reference.Domain);
        var itemId = VisibleString(reference.Item);

        // ObjectName.domain-specific [1] ::= SEQUENCE { domainId Identifier, itemId Identifier }
        var domainSpecificObjectName = Wrap(0xA1, Concat(domainId, itemId));

        // VariableSpecification.name [0] ObjectName
        var variableSpecificationName = Wrap(0xA0, domainSpecificObjectName);

        // SEQUENCE OF VariableSpecification. Public packet traces show the sequence wrapper here
        // (for example ... A1 len A0 len 30 len A0 len A1 len ...), and strict IEDs may reject
        // the earlier compact form without this SEQUENCE.
        var variableSpecificationSequence = Wrap(0x30, variableSpecificationName);

        // VariableAccessSpecification.listOfVariable [0] IMPLICIT SEQUENCE OF VariableSpecification.
        var listOfVariable = Wrap(0xA0, variableSpecificationSequence);

        // Read-Request.variableAccessSpecification [1] VariableAccessSpecification.
        var variableAccessSpecification = Wrap(0xA1, listOfVariable);

        byte[] readRequestBody;
        if (includeSpecificationWithResult)
        {
            // Read-Request.specificationWithResult [0] IMPLICIT BOOLEAN DEFAULT FALSE.
            // TRUE is useful as an explicit interop profile because some commercial drivers expose
            // this as a tunable IEC 61850 read option.
            var specificationWithResultTrue = new byte[] { 0x80, 0x01, 0xFF };
            readRequestBody = Concat(specificationWithResultTrue, variableAccessSpecification);
        }
        else
        {
            readRequestBody = variableAccessSpecification;
        }

        // ConfirmedServiceRequest.read [4]
        var readRequest = Wrap(0xA4, readRequestBody);

        // Confirmed-RequestPDU [0] ::= SEQUENCE { invokeID Unsigned32, confirmedServiceRequest ... }
        var invoke = Integer(invokeId);
        return Wrap(0xA0, Concat(invoke, readRequest));
    }

    private static byte[] WrapForPayloadProfile(byte[] mmsPdu, MmsReadPayloadProfile payloadProfile)
    {
        return payloadProfile switch
        {
            MmsReadPayloadProfile.PresentationDataValues or MmsReadPayloadProfile.PresentationDataValuesWithSpecificationResult
                => WrapIsoPresentationPData(mmsPdu),
            MmsReadPayloadProfile.SessionDataOnly
                => Concat(new byte[] { 0x01, 0x00 }, mmsPdu),
            MmsReadPayloadProfile.RawMmsPdu
                => mmsPdu,
            _ => WrapIsoPresentationPData(mmsPdu)
        };
    }

    /// <summary>
    /// ISO Session data SPDU + ISO Presentation fully-encoded-data with context id 3
    /// mapped to MMS abstract syntax 1.0.9506.2.3 negotiated during ACSE.
    /// </summary>
    public static byte[] WrapIsoPresentationPData(byte[] mmsPdu, int presentationContextId = 3)
    {
        var contextId = Integer(presentationContextId);
        var singleAsn1Type = Wrap(0xA0, mmsPdu); // presentation-data-values: single-ASN1-type [0]
        var pdvList = Wrap(0x30, Concat(contextId, singleAsn1Type));
        var fullyEncodedData = Wrap(0x61, pdvList); // User-data [APPLICATION 1]

        // Session data-transfer SPDU (01 00) followed by the presentation user-data selector (01 00)
        // used in captured IEC 61850 MMS P-DATA frames.
        return Concat(new byte[] { 0x01, 0x00, 0x01, 0x00 }, fullyEncodedData);
    }

    private static byte[] Integer(int value)
    {
        if (value < 0 || value > 0x7FFFFFFF) throw new ArgumentOutOfRangeException(nameof(value));
        if (value <= 0x7F) return new byte[] { 0x02, 0x01, (byte)value };
        if (value <= 0xFF) return new byte[] { 0x02, 0x02, 0x00, (byte)value };
        if (value <= 0x7FFF) return new byte[] { 0x02, 0x02, (byte)(value >> 8), (byte)value };
        if (value <= 0xFFFF) return new byte[] { 0x02, 0x03, 0x00, (byte)(value >> 8), (byte)value };
        return new byte[] { 0x02, 0x04, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
    }

    private static byte[] VisibleString(string text)
        => Wrap(0x1A, System.Text.Encoding.ASCII.GetBytes(text));

    private static byte[] Wrap(byte tag, byte[] value)
    {
        var writer = new BerWriter();
        writer.WriteTlv(tag, value);
        return writer.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts) length += part.Length;
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return result;
    }
}
