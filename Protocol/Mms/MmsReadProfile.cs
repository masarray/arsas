namespace ArIED61850Tester.Protocol.Mms;

public enum MmsReadProfile
{
    PrimaryFcNamedVariable,
    AlternateNoFcNamedVariable
}

public enum MmsReadPayloadProfile
{
    /// <summary>Correct ISO Session + ISO Presentation P-DATA PDV-list wrapper carrying MMS single-ASN1-type.</summary>
    PresentationDataValues,

    /// <summary>Same P-DATA wrapper, but MMS Read carries explicit specificationWithResult=true for interop profiling.</summary>
    PresentationDataValuesWithSpecificationResult,

    /// <summary>Session data-transfer header only. Diagnostic fallback for strict presentation-profile tuning.</summary>
    SessionDataOnly,

    /// <summary>Raw MMS PDU without ISO Session/Presentation envelope. Diagnostic fallback only.</summary>
    RawMmsPdu
}

public sealed class MmsReadAttempt
{
    public MmsReadProfile Profile { get; init; }
    public MmsReadPayloadProfile PayloadProfile { get; init; }
    public MmsObjectReference Reference { get; init; }
    public string RequestHexPreview { get; init; } = string.Empty;
    public MmsReadDecodeResult Result { get; init; } = new();

    public string Summary => $"{Profile}/{PayloadProfile}: {Reference} => {(Result.IsSuccess ? "OK" : Result.Message)}";
}
