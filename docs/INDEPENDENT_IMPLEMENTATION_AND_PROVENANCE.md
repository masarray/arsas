# Independent implementation and provenance policy

ARSAS is an independently maintained, GPL-3.0-or-later IEC 61850 engineering workstation. Engineering compatibility is measured against the protocol and reproducible device behavior, not by copying another application's source, screen, wording, branding, or protected material.

This policy supplements [CONTRIBUTING.md](../CONTRIBUTING.md), [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md), [SECURITY.md](../SECURITY.md) and [AGENTS.md](../AGENTS.md). It defines review requirements; it is **not** a blanket legal or originality certification.

## Inputs that can be used

- Standards-based protocol semantics, independently implemented from lawfully available specifications and observed protocol behavior.
- Authorized black-box interoperability measurements, with dates, scope, device/lab context and original source identification recorded in historical provenance when relevant.
- Synthetic, independently generated SCL, MMS, report, COMTRADE and UI test fixtures with a documented generator or author.
- Third-party dependencies or assets only when their license, origin and required notices have been checked and preserved.

## Inputs that must not be imported

Do not copy unrelated applications' source, algorithms expressed as implementation code, decompiled code, compiled binaries, SDK internals, UI assets, screenshots, text, logos, report layouts, or protected examples. Do not publish confidential device configurations, customer identity, credentials, raw proprietary field captures or private network addresses. A competitor's observable behavior may motivate an interoperability test, but its source or visual design is not an implementation specification.

Recreating a functional interaction independently does not justify claiming affiliation, endorsement, certification, or code lineage with the tool used for comparison.

## Provenance categories and naming

| Category | Active-tree policy |
| --- | --- |
| Product UI, website, marketing, class/test/workflow names | ARSAS or neutral engineering terminology; no unneeded competitor branding. |
| Project-owned synthetic fixtures | Neutral names, independently generated content and deterministic assertions. |
| Physical acceptance measurements | Preserve counts, timestamp/provenance, source SHA, engine lock, DataSet order, report outcomes and earlier rejection records. |
| Historical third-party comparison | Use a neutral reference alias in active contracts only with a documented mapping to the actual original comparison in immutable historical evidence. Never imply a different source generated the measurements. |
| Required notices, licenses and third-party dependencies | Preserve accurate attribution and license text, even when it contains a third-party name. |
| Historical Git commits, tags and releases | Retain exact identity; do not rewrite source/release hashes to cosmetically remove a reference. |

A name-only migration must enumerate all consumers, including workflow triggers, required check names, JSON keys, scripts, test paths, website output and links. Record before/after semantic equality for numerical evidence and a precise old-to-new key map. Run both unit/contract and full Windows CI. Where a change affects reporting, Discovery, SCL or engine behavior, perform the separate physical acceptance required by its subsystem.

## Contribution evidence checklist

For new source, test or asset files, identify who authored it, whether any third-party material was used, relevant licenses, and how it was independently produced. For generated fixtures, retain the generator or document its exact procedure. For a source-derived claim, provide a source and distinguish a physical measurement from a simulated fixture or an inference.

A passing keyword scan is only one control. Reviewers must also inspect source similarity, dependencies/SBOM, fonts/assets, screenshots, website claims and the release manifest. Suspected unlicensed material must be quarantined from future packages and reviewed before release; do not remove required license notices as a way of making a scan pass.

## Stable release boundary

The published v1.6.40 app/engine identity and its accepted field evidence are historical release records. Repository naming and maintainability work does not retroactively change its binary, tag or field results. A future release requires its own immutable source and artifact provenance.
