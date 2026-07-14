# Third-Party Notices

ArIED 61850 is distributed under `GPL-3.0-or-later`. Its licensing does not change the license of ARIEC61850 or any included third-party package, standard, sample, font, image, or asset. Each component remains subject to its own license and attribution requirements.

## ARIEC61850 engine

ArIED references the separately maintained ARIEC61850 source project at build time. Distributed combined builds must comply with the license and notices of the exact ARIEC61850 revision that is packaged. The application repository does not replace, relicense, or conceal the engine.

## External intellectual-property boundary

No source code, binary, header, generated binding, wrapper, example, test, API layer, executable, manual, brochure, help file, screenshot, icon, logo, product photo, report template, UI resource, database, capture, or extracted asset from an unrelated external implementation or proprietary engineering product is included or directly required by this application repository.

Interoperability testing with separately licensed tools does not make those tools application dependencies and does not authorize copying their software, documentation, visual design, reports, resources, or confidential data.

## Assets and releases

All application icons, screenshots, illustrations, UI resources, and marketing images included in a release must be project-owned or separately licensed for that use. Screenshots must be generated from ArIED itself using synthetic or sanitized data.

Before every public or commercial release:

1. review the exact ARIEC61850 engine revision and its dependency graph;
2. run `scripts/verify-source-clean.ps1`;
3. inspect the portable archive for unexpected binaries, captures, manuals, logs, screenshots, or customer data;
4. preserve required license and attribution documents; and
5. confirm compliance with `docs/CLEAN_ROOM_AND_INTEROPERABILITY_POLICY.md`.
