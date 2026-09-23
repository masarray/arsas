# ARSAS LinkedIn relaunch — reviewed publication copy

Status: ready for Ari Sulistiono to review and post manually. This is editorial material, **not** a new product release or a claim that a physical field retest was performed.

## Copy-ready LinkedIn post (plain text; no Markdown bold syntax)

```text
IEC 61850 engineering software can be EXPENSIVE.

What if you could discover an IED, download AND analyze COMTRADE, and prepare FAT evidence—all in one FREE, OPEN-SOURCE workstation?

Meet the upgraded ARSAS. Not just another IED tester.

🔌 NO CID FILE? START WITH THE IP ADDRESS.

Smart Discovery connects to an accessible IEC 61850 MMS device, builds the observed live model, and lets you monitor signals and explore DataSets and RCBs.

Generate Edition 1 ICD or Edition 2 IID as a reusable device baseline. Especially useful in brownfield substations when the original engineering files are missing or outdated.

📈 DOWNLOAD. OPEN. ANALYZE COMTRADE.

Retrieve disturbance records from the IED and open them inside ARSAS. Inspect waveforms, RMS, phasors, harmonics, distance locus, and protection event timelines—without launching a separate viewer.

⚡ MORE THAN MMS.

Explore RCBs, monitor GOOSE and live values, investigate events, and evaluate Sampled Values (engineering preview).

📋 FAT EVIDENCE, NOT JUST SCREENSHOTS.

Capture Value 1/2 with quality and timestamps; review supporting file-service and time-synchronization evidence.

Preview the report, add your own company logo, and export a printable PDF for FAT/SAT documentation—all within the same engineering workstation.

FREE public Windows download. Open-source code under GPL-3.0-or-later.

🔗 Download: https://masarray.github.io/arsas/
💻 Source: https://github.com/masarray/arsas

For protection and substation automation engineers: which workflow would save you the most time—brownfield discovery, COMTRADE analysis, or FAT evidence?

#IEC61850 #SubstationAutomation #OpenSource
```

## Actual repository visual assets (do not invent a UI or a report)

Use screenshots from the *same v1.6.40* asset set. Open original links to check legibility and redact any unauthorized project/device identity before posting.

1. Primary COMTRADE visual: [time signals](../Assets/screenshot/arsas-comtrade-time-signals-v1.6.40.webp) or [protection timeline](../Assets/screenshot/arsas-comtrade-protection-timeline-v1.6.40.webp). Show that records are *opened and analyzed*, not only downloaded.
2. Smart Discovery: [IP connect workflow](../Assets/screenshot/arsas-quick-start-discover-ip-v1.6.40.webp) followed by [IED Explorer](../Assets/screenshot/arsas-ied-explorer-command-v1.6.40.webp).
3. SCL: [Edition 1 / Edition 2 export](../Assets/screenshot/arsas-scl-edition-export-v1.6.40.webp).
4. FAT: [native FAT workspace](../Assets/screenshot/arsas-native-fat-v1.6.40.webp) and [real PDF report preview](../Assets/screenshot/arsas-fat-report-preview-v1.6.40.webp).

Recommended clean layout: one readable hook, one large *actual* product screenshot, two small supporting screenshots, free/open-source label and a single website CTA. Put detail in the post body, not as tiny text in a three-column poster.

## Evidence and claim boundaries

- Stable public version at preparation: v1.6.40. Never treat newer `main` features as available in that tagged package without checking.
- Discovery needs an accessible IEC 61850 MMS device and supported services; it does not guarantee success with every vendor/IED.
- The exported Edition 1 ICD / Edition 2 IID reflects the observed single-IED model; it is **not** a reconstruction of the complete historical project CID/SCD.
- In-process COMTRADE analysis is available in the current product; no external viewer process is required for the listed analysis workflow.
- GOOSE is available; Sampled Values is an **engineering preview**, not production-grade calibrated scaling or universal stream compatibility.
- FAT Value 1/2 evidence and supporting file/time-sync evidence are distinct, reviewable records. Do not claim automatic full FAT execution of every IEC 61850 service or official acceptance/certification.
- Preview and PDF share a report layout; the Add Logo action replaces the logo for that preview and saved PDF.
- The public download is free and the code is under GPL-3.0-or-later. Do not imply that GPL obligations or corporate execution/security policies disappear.
- The binaries are not Authenticode-signed; refer users to the exact release and published SHA-256.
- Do not claim that ARSAS is the only such software in the world or make unsupported comparisons with specific commercial packages.

## Release-note check

As checked on 2026-09-23, `landing/latest.json`, `landing/release-notes.json` and the public Release Notes all identify stable **v1.6.40**. No version rewrite, engine pin change, release rebuild, or runtime change is needed for this copy adjustment.
