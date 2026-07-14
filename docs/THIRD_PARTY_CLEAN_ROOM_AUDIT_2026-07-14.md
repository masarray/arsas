# Third-Party Clean-Room Audit — 2026-07-14

This is a repository-evidence and process audit, not a legal opinion or a guarantee against every possible claim.

## Scope

The audit reviewed the current ArIED 61850 application repository for contamination or confusion risk associated with:

- libiec61850 and other external IEC 61850 protocol stacks;
- OMICRON IEDScout, SVScout, and StationScout;
- copied source, API structures, manuals, screenshots, icons, logos, visual layouts, report templates, wording, binaries, captures, and affiliation claims;
- the application's direct source-level dependency on the independently maintained ARIEC61850 engine.

## Findings

At the audit date:

- no libiec61850 source, binary, header, generated binding, wrapper, package reference, or direct API identifier was detected in the current application tree;
- ArIED references ARIEC61850 through a .NET project reference and does not directly reference an external IEC 61850 protocol stack;
- no direct third-party NuGet `PackageReference` was detected in the ArIED application project;
- no OMICRON executable, library, manual, brochure, screenshot, icon, logo, product photo, help file, report template, or extracted resource was detected in the tracked application repository;
- no current source or public application documentation identifies IEDScout, SVScout, or StationScout as an implementation source or application dependency;
- the tracked application assets use ArIED-owned names and resources;
- the repository author audit found no external human contributor whose copyright permission must be obtained for the present licensing model.

These findings support an independently developed application position. Automated repository scans cannot prove the absence of undisclosed off-repository material, contractual restrictions, or all visual similarity.

## libiec61850 boundary

ArIED must not incorporate, link, translate, mechanically port, wrap, or imitate libiec61850 source, bindings, examples, tests, API structures, naming schemes, binaries, or documentation. The application is designed against ARIEC61850's own managed C# API and must not present itself as a libiec61850 derivative, wrapper, compatible API, or commercially authorized edition.

Standards-based feature overlap is expected among IEC 61850 tools. The defensible distinction is independently authored code, architecture, tests, naming, UI, and provenance records.

## OMICRON proprietary-tool boundary

IEDScout, SVScout, and StationScout are separate proprietary products. Their manuals, screenshots, UI composition, icons, logos, report styles, help wording, internal resources, product photos, and software are not application design inputs and must not be included in the repository, website, documentation, or package.

Lawfully licensed black-box interoperability tests may be performed in an isolated laboratory subject to the applicable EULA and organizational policy. They must not involve reverse engineering, resource extraction, decompilation, disassembly, memory inspection, database extraction, technical-restriction circumvention, or copying protected expression.

Any observed protocol behavior must be independently reduced to a standards fact and implemented in ARIEC61850, not copied into the ArIED application layer from a proprietary client.

## User-interface review

Common functional patterns such as an IED model tree, signal grid, event list, report monitor, waveform display, phasor plot, SCL view, and command panel are engineering concepts. Copyright risk arises from copying the distinctive expression of those concepts.

ArIED must retain its own:

- information architecture and workflow;
- spacing, sizing, colors, typography, icons, and visual hierarchy;
- pane composition and interaction sequence;
- labels, instructional text, diagnostics wording, and report appearance;
- screenshots and marketing artwork generated from ArIED itself.

No proprietary screenshot may be used as a pixel-level design reference, landing-page asset, documentation image, or release illustration.

## Trademark and market-positioning review

Third-party product names may appear only in reviewed legal or factual interoperability statements. ArIED must not use OMICRON or MZ Automation logos, confusingly similar product naming, unsupported comparison claims, or wording suggesting certification, partnership, sponsorship, compatibility approval, or endorsement.

Recommended statement:

> ArIED 61850 and ARIEC61850 are independently developed, vendor-neutral engineering tools. They are not affiliated with, sponsored by, certified by, or endorsed by MZ Automation, OMICRON, or any other IEC 61850 tool vendor.

## Controls added

- added `docs/CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md`;
- expanded `THIRD_PARTY_NOTICES.md` with external-stack, proprietary-tool, and non-affiliation boundaries;
- added `scripts/verify-source-clean.ps1` to reject prohibited product-named files, copied assets, captures, binaries, and unreviewed product references;
- integrated source-clean verification into the Windows build workflow before restore and publish;
- retained licensing and attribution documents in portable packages.

## Remaining manual checks

Before a high-value commercial, OEM, or white-label transaction, manually review:

- applicable product EULAs and account/download terms for every proprietary tool used in testing;
- the full ARIEC61850 engine revision included in the distributed build;
- employment, invention-assignment, moonlighting, confidentiality, and customer-data obligations;
- private screenshots, design notes, SCL, PCAP, training materials, support responses, and test artifacts that are not visible in GitHub;
- visual similarity through an independent UI/trade-dress review;
- trademark clearance for ArIED 61850 and related branding;
- release archives for unexpected binaries, images, manuals, logs, or customer data.

## Conclusion

No current repository evidence was found that ArIED contains or directly depends on libiec61850, or that it bundles OMICRON software, manuals, screenshots, branding, or UI assets. The strongest remaining risk is off-repository provenance and contractual restrictions that GitHub cannot establish. The controls above materially reduce copyright, license, trademark, and affiliation risk but do not replace professional legal review for a significant commercial agreement.
