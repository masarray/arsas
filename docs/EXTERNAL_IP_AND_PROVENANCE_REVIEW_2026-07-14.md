# External IP and Provenance Review — 2026-07-14

This document records repository evidence and project controls. It is not a legal opinion or a guarantee against every possible claim.

## Scope

The review examined the current ArIED 61850 application repository for contamination or confusion risk associated with unrelated implementations, proprietary engineering products, copied source or API structures, manuals, screenshots, icons, logos, visual layouts, report templates, wording, binaries, captures, and affiliation claims.

## Repository findings

At the review date:

- no unrelated implementation source, binary, header, generated binding, wrapper, package reference, or direct API identifier was detected in the current application tree;
- ArIED references ARIEC61850 through a .NET project reference and does not directly reference another IEC 61850 protocol implementation;
- no direct third-party NuGet `PackageReference` was detected in the application project;
- no proprietary executable, library, manual, brochure, screenshot, icon, logo, product photo, help file, report template, or extracted resource was detected in the tracked repository;
- no current source or public documentation identifies an external commercial product as an implementation source or application dependency;
- tracked application assets use ArIED-owned names and resources;
- the repository author review found no external human contributor whose permission was identified as required for the current licensing model.

These findings support an independently developed application position. Automated repository scans cannot establish the absence of undisclosed off-repository material, contractual restrictions, or every form of visual or conceptual similarity.

## External implementation boundary

ArIED must not incorporate, link, translate, mechanically port, wrap, or imitate source, bindings, examples, tests, API structures, naming schemes, binaries, or documentation from unrelated implementations. The application is designed against ARIEC61850's independently maintained managed C# API.

Standards-based functional overlap is expected among IEC 61850 tools. The defensible distinction is independently authored code, architecture, tests, naming, UI, fixtures, and provenance records.

## Proprietary-material boundary

Proprietary software and its manuals, screenshots, UI composition, icons, logos, report styles, help wording, internal resources, product photos, and extracted assets are not application design inputs and must not be included in the repository, website, documentation, or release package.

Lawfully licensed black-box interoperability testing may be performed in an isolated laboratory subject to the applicable license and organizational policy. It must not involve resource extraction, decompilation, disassembly, memory inspection, database extraction, technical-restriction circumvention, or copying protected expression.

Observed protocol behavior must be reduced to a vendor-neutral standards fact and implemented in ARIEC61850, not copied into the ArIED application layer from an external client.

## User-interface boundary

Common functional patterns such as an IED tree, signal grid, event list, report monitor, SCL view, command panel, waveform, or phasor display are engineering concepts. Copyright and trade-dress risk arises from copying the distinctive expression of those concepts.

ArIED must retain its own information architecture, workflow, spacing, sizing, colors, typography, icons, visual hierarchy, pane composition, interaction sequence, labels, diagnostics wording, report appearance, screenshots, and marketing artwork.

External product screenshots must not be used as pixel-level design specifications, landing-page assets, documentation images, or release illustrations.

## Repository controls

- maintain a vendor-neutral clean-room and interoperability policy;
- list only dependencies actually used by the application;
- reject restricted external identifiers from normal source and documentation through fingerprint-based scanning;
- reject tracked captures, manuals, binaries, logs, and confidential evidence;
- run source-clean verification before restore, build, publish, and artifact upload;
- retain licensing and attribution documents in portable packages;
- use synthetic or contributor-owned examples in public documentation and tests.

## Remaining manual checks

Before a high-value commercial, OEM, or white-label transaction, manually review:

- applicable software licenses and account or download terms for every external tool used in testing;
- the exact ARIEC61850 engine revision included in the distributed build;
- employment, invention-assignment, moonlighting, confidentiality, and customer-data obligations;
- private screenshots, design notes, SCL, captures, training materials, support responses, and test artifacts not visible in GitHub;
- visual similarity through an independent UI and trade-dress review;
- trademark clearance for ArIED 61850 and related branding;
- release archives for unexpected binaries, images, manuals, logs, or customer data.

## Conclusion

No current repository evidence was found that ArIED contains or directly depends on unrelated implementation code or bundles proprietary software, manuals, screenshots, branding, report templates, or UI resources. The strongest remaining risk is off-repository provenance and contractual restrictions that repository inspection cannot establish. The controls above materially reduce copyright, license, trademark, and affiliation risk but do not replace professional legal review for a significant commercial agreement.
