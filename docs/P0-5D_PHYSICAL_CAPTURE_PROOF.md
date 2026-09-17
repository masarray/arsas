# P0-5d — Physical Capture Request-Budget & Duplicate-Wire Proof

Status: field-capture evidence lane for ARSAS PR #324. This phase does not change MMS discovery semantics. It proves, from a fresh PCAP, that the P0-5b engine request-budget work and P0-5c ARSAS association single-flight are actually visible on the wire.

## Immutable test baseline

- ARSAS branch: `test/smart-ied-discovery-pr134`
- ARIEC61850 PR: #134
- Engine commit: `4467124775d8d9d76f3db194f9fbfd97144767a8`
- Engine `.NET CI`: #652 passed
- ARSAS P0-5c Smart Discovery Field Capture Build: #29 passed at ARSAS commit `42c8f54177dd8f1c7570277e44cb95393427197b`

The P0-5d verifier is additive test tooling. It does not send MMS traffic.

## What must be captured

Capture the complete interval from before TCP/ACSE/MMS association establishment until the first smart discovery has completed. For the clean discovery proof, do not start reporting, polling, control inspection, or command execution during the capture.

Recommended Wireshark capture filter when the IED address is known:

```text
host <IED-IP> and tcp port 102
```

Save the result as `.pcapng` without trimming the beginning or end of the association.

## Wire proof contract

`scripts/verify-smart-discovery-pcap.ps1` decodes MMS with TShark and evaluates the client-to-server confirmed-request stream. A P0-5d PASS requires:

1. exactly one TCP stream carrying client MMS confirmed requests;
2. zero repeated semantic confirmed requests after invoke-ID is excluded from the fingerprint;
3. zero repeated GetNameList semantic requests — the proxy for a second naming sweep;
4. zero repeated GetVariableAccessAttributes semantic requests;
5. no invoke-ID reuse while the previous request is still outstanding;
6. no orphan response/error and no request left outstanding when capture ends;
7. measured peak outstanding requests does not exceed the MMS `negociatedMaxServOutstandingCalling` value when Wireshark exposes it;
8. optional explicit total-request and GVA budgets are respected;
9. optional IEDScout reference comparison is emitted from the same verifier.

The semantic request fingerprint includes service, object class/scope, domain, item/object item identity and continuation markers. It deliberately excludes `mms.invokeID`, so the same logical request sent twice with different invoke IDs is still detected as duplicate traffic.

## Run the proof

From the ARSAS repository or from the field-capture artifact bundle:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-smart-discovery-pcap.ps1 `
  -PcapPath .\ARSAS_P0-5d.pcapng
```

To compare the same IED against an IEDScout capture:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-smart-discovery-pcap.ps1 `
  -PcapPath .\ARSAS_P0-5d.pcapng `
  -ReferencePcapPath .\IEDScout_DiscoveryIED.pcapng
```

Optional hard budgets can be imposed after the first clean same-IED run establishes the expected envelope:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-smart-discovery-pcap.ps1 `
  -PcapPath .\ARSAS_P0-5d.pcapng `
  -MaxConfirmedRequests <budget> `
  -MaxGvaRequests <budget>
```

Do not use `-RequireNoMoreRequestsThanReference` as a universal correctness rule. Different valid discovery strategies can use different service mixes. It is available only for a deliberately chosen same-IED acceptance contract.

## Output

The verifier writes `P0-5D-<capture-name>-proof.json` beside the ARSAS capture. The JSON contains:

- inferred client/server endpoints and request TCP stream(s);
- confirmed request/response counts;
- counts by MMS service;
- semantic duplicate details with frame numbers;
- duplicate GetNameList and GVA counts;
- second-sweep detection;
- peak outstanding requests;
- negotiated calling limit when present;
- invoke-ID lifecycle anomalies;
- unanswered/orphan counts;
- optional ARSAS-vs-reference request-count, peak-outstanding and service-budget deltas;
- final PASS/FAIL plus every failed acceptance gate.

Keep the raw PCAP and generated proof JSON together. The JSON is derived evidence; the PCAP remains authoritative.

## Field acceptance for the golden relay

For the AA1E1F06R4 comparison, P0-5d is not considered physically proven until a fresh capture made with the exact P0-5d artifact passes the wire contract and the discovered model is separately checked against the canonical semantic target used throughout PR #134. Do not transfer an older R1/R2 capture result to a newer ARSAS or engine SHA.

The first clean P0-5d result should be used to establish an evidence-backed same-IED hard request budget. That number should then be locked in a later regression/fixture rather than guessed in protocol code.
