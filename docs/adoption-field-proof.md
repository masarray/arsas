# ARSAS P3 — Adoption and field proof

P3 connects discovery and download with first-run success, bounded compatibility evidence and structured field feedback.

## Public adoption path

The website provides four bilingual entry pairs:

- `quick-start.html` and `panduan-mulai-arsas.html`;
- `faq.html` and `faq-arsas.html`;
- `compatibility.html` and `bukti-kompatibilitas.html`;
- `demo.html` and `demo-arsas.html`.

Quick Start is read-first. It verifies the package, confirms the authorized network path and TCP port 102, discovers the live MMS model, observes one attributable value, distinguishes reporting from polling and stops before any unauthorized write or control.

The guided demo uses real application screenshots. It is not a simulated relay session and makes no time-to-value claim.

## Compatibility evidence governance

`landing/device-evidence.json` is the machine-readable evidence registry. Compatibility is stated per service, date and condition using this vocabulary:

- verified;
- conditional;
- observed;
- not tested;
- failed with known issue.

The initial registry contains two anonymized field profiles and zero publicly named device models. A named vendor, model, firmware or site is not added without adequate evidence and disclosure permission. Verified does not mean IEC 61850 conformance certification.

## Private guide search

The English and Indonesian guide hubs provide local browser search and category filtering. Search text is not sent to a server and is not emitted as an analytics event.

## Structured issue intake

GitHub Issue Forms separate device compatibility, connection, reporting, file transfer, GOOSE, installation and feature requests. Forms require version and environment evidence plus confirmation that credentials and confidential project data were removed.

Security-sensitive reports continue through private GitHub Security Advisories.

## Responsive media

`scripts/build-responsive-media.py` creates multiple WebP widths for each product screenshot and injects `srcset` and `sizes` into the built site. `scripts/validate-responsive-media.py` verifies every rendered screenshot candidate and records counts in `build-info.json`.

## Supply-chain evidence

The current stable release keeps its existing package name, size, SHA-256, publication and Authenticode evidence. P3 does not retroactively claim an SBOM or provenance attestation for an older release.

For an exact published tag, `.github/workflows/release-supply-chain.yml`:

1. checks out the exact tag;
2. downloads the official installer, portable ZIP and checksum file;
3. verifies the published SHA-256 values;
4. inventories the extracted portable package into SPDX 2.3 JSON;
5. uploads the SPDX file to the same tagged release;
6. creates GitHub artifact digest and SBOM attestations for the verified package files.

This is checksum-verified post-publication artifact evidence. It is not described as reproducible build provenance and is not IEC 61850 conformance certification.
