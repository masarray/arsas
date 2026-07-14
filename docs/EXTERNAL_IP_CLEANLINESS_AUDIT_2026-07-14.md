# External IP Cleanliness Audit — 2026-07-14

This is a repository-evidence and process audit, not a legal opinion or a guarantee against every possible claim.

## Scope

The audit reviewed the current ArIED 61850 application repository for contamination or confusion risk associated with unrelated external implementations, proprietary engineering products, copied source, API structures, manuals, screenshots, icons, logos, visual layouts, report templates, wording, binaries, captures, and affiliation claims.

## Findings

At the audit date:

- no unrelated external implementation source, binary, header, generated binding, wrapper, package reference, or direct API identifier was detected in the current application tree;
- ArIED references ARIEC61850 through a .NET project reference and does not directly reference another protocol implementation;
- no direct third-party NuGet `PackageReference` was detected in the ArIED application project;
- no proprietary executable, library, manual, brochure, screenshot, icon, logo, product photo, help file, report template, or extracted resource was detected in the tracked application repository;
- no current source or public application documentation identifies an external commercial product as an implementation source or application dependency;
- the tracked application assets use ArIED-owned names and resources;
- the repository author audit found no external human contributor whose copyright permission must be obtained for the present licensing model.

These findings support an independently developed application position. Automated repository scans cannot prove the absence of undisclosed off-repository material, contractual restrictions, or all visual similarity.

## External implementation boundary

ArIED must not incorporate, link, translate, mechanically port, wrap, or imitate source, bindings, examples, tests, API structures, naming schemes, binaries, or documentation from unrelated external implementations. The application is designed against ARIEC61850's own managed C# API.

Standards-based feature overlap is expected among IEC 61850 tools. The defensible distinction is independently authored code, architecture, tests, naming, UI, and provenance records.

## Proprietary-tool boundary

Proprietary products and their manuals, screenshots, UI composition, icons, logos, report styles, help wording, internal resources, product photos, and software are not application design inputs and must not be included in the repository, website, documentation, or package.

Lawfully licensed black-box interoperability tests may be performed in an isolated laboratory subject to the applicable license and organizational policy. They must not involve reverse engineering, resource extraction, decompilation, disassembly, memory inspection, database extraction, technical-restriction circumvention, or copying protected expression.

Any observed protocol behavior must be independently reduced to a standards fact and implemented in ARIEC61850, not copied into the ArIED application layer from a proprietary client.

## User-interface review

Common functional patterns such as an IED model tree, signal grid, event list, report monitor, waveform display, phasor plot, SCL view, and command panel are engineering concepts. Copyright risk arises from copying the distinctive expression of those concepts.

ArIED must retain its own information architecture, workflow, spacing, sizing, colors, typography, icons, visual hierarchy, pane composition, interaction sequence, labels, diagnostics wording, report appearance, screenshots, and marketing artwork.

No proprietary screenshot may be used as a pixel-level design reference, landing-page asset, documentation image, or release illustration.

## Controls

- maintain a vendor-neutral clean-room and interoperability policy;
- maintain dependency notices only for components actually used by the application;
- reject external-product identifiers from normal source and documentation through fingerprint-based scanning;
- reject tracked captures, manuals, binaries, logs, and confidential evidence;
- run source-clean verification before restore, build, publish, and artifact upload;
- retain licensing and attribution documents in portable packages.

## Remaining manual checks

Before a high-value commercial, OEM, or white-label transaction, manually review:

- applicable product licenses and account/download terms for every proprietary tool used in testing;
- the full ARIEC61850 engine revision included in the distributed build;
- employment, invention-assignment, moonlighting, confidentiality, and customer-data obligations;
- private screenshots, design notes, SCL, PCAP, training materials, support responses, and test artifacts that are not visible in GitHub;
- visual similarity through an independent UI and trade-dress review;
- trademark clearance for ArIED 61850 and related branding; and
- release archives for unexpected binaries, images, manuals, logs, or customer data.

## Conclusion

No current repository evidence was found that ArIED contains or directly depends on unrelated external implementation code or bundles proprietary software, manuals, screenshots, branding, report templates, or UI resources. The strongest remaining risk is off-repository provenance and contractual restrictions that repository inspection cannot establish. The controls above materially reduce copyright, license, trademark, and affiliation risk but do not replace professional legal review for a significant commercial agreement.
