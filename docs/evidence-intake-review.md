# ARSAS service-evidence intake and review

This is the public review procedure for [compatibility evidence](https://masarray.github.io/arsas/compatibility.html) and the [device compatibility issue form](https://github.com/masarray/arsas/issues/new?template=device-compatibility.yml). It is a documentation and maintainer-review workflow, not an automatic test, endorsement, vendor certification, or IEC 61850 conformance process.

## 1. Submit one bounded service result

Open one issue per device context and IEC 61850 service. Record the **actual test date**, exact ARSAS version/tag, Windows version, anonymized device/firmware or disclosure boundary, service, observed outcome, acquisition conditions, expected versus observed behavior, and sanitized diagnostics or a public evidence link. Include negative results and reproducible steps. State whether the submission is a field test, sanitized diagnostic, engineering history, or a planned test with no result. If retesting a published profile, supply its ID; do not overwrite historical dates or assume that the latest release was the version used.

**Privacy first:** never publish credentials, private IPs/endpoints, customer/project names, confidential SCL, raw network captures, or disturbance records. Review screenshots, text and attachments for sensitive identifiers before posting. If safe publication is impossible, report only a bounded sanitized description and mark the evidence unavailable for public verification. Do not upload private files and ask the maintainer to sanitize them publicly.

A newly opened issue is **submitted, not verified**. An issue link alone does not establish a physical-relay test or a new compatibility status. Planned tests and implementation-only history cannot serve as a current-stable field retest.

## 2. Maintainer review gate

Review the report before considering any registry change:

- **Privacy and authority:** safe-to-publish identifiers; approved network/test context; control tests only in an authorized non-energized environment.
- **Provenance:** actual date, exact tested ARSAS version, evidence type, device disclosure boundary and traceable public record; identify whether raw field capture is publicly linked.
- **Scope and reproducibility:** one service, explicit positive/negative outcome, conditions, permissions, relevant MMS/RCB/GOOSE/control paths, expected versus observed behavior.
- **Claim precision:** separate *observed*, *conditional*, *verified*, *known issue*, *not tested* and *not declared*; a missing service is not a tested failure. Reporting configuration is not proof of live report delivery.
- **Retest:** confirm actual repeat execution on the declared release; engineering PRs or a registry edit are not a retest. Keep earlier evidence dates and unknowns intact.

If details are missing, request sanitized clarification in the issue and leave the matrix unchanged. If a submission cannot be publicly inspected or responsibly bounded, keep it as a report only, not a published compatibility result. Do not represent maintainer review as independent certification.

## 3. Promote only with a reviewed PR

A maintainer may open a separate PR to update `landing/device-evidence.json` and the English/Indonesian compatibility pages. In that PR:

1. Cite the reviewed public issue or sanitized record *for the exact service*, with test date, version, conditions and record type. A private attachment is not a public evidence link.
2. Set or retain each service status based on its own observation. Do not turn `not-tested` or `not-declared` into a claim by copying another service's result.
3. Change `testedArsasVersion`, `lastRetest`, `rawFieldCaptureLinked` or current-stable language only when the specific supporting public test and date exist. Distinguish engineering history from raw field captures.
4. Reconcile registry, service-to-record links, coverage gaps, English/Indonesian pages and claim boundaries; run source, rendered-site, adoption/field-proof and exact-head PR CI.
5. Merge only after review and required checks pass; verify the final production Pages deployment. The published matrix is authoritative only after this gate.

An issue may remain open or be closed without a registry change. A green CI validates consistency of the published claims and links, **not the physical truth of a device test**.
