# ARSAS Engineering D25 → D26: exact-tree Windows and package evidence qualified

2026-10-27 WIB. Continues [D24](ARSAS_RECONCILIATION_D24_2026-10-27.md). Evidence-only milestone; no PR merge, release, engine repin, runtime changes or new CI runs.

## Exact source and synthetic merge correspondence

- [P7.6 PR #759](https://github.com/masarray/arsas/pull/759) remains OPEN, READY FOR REVIEW, NOT MERGED; PR HEAD `43b7eed442713194f76ea115ce3bf5cecdc6f5cf` and RC base `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`.
- Actual checkout in Windows CI #40427176051: GitHub-generated PR synthetic merge `b44df9716b5ce201c392522e02b8998eb06dfb10`, whose Git parents are **RC base** `817c3910...` and **PR HEAD** `43b7eed...`.
- Direct Git commit lookup confirms **the same tree SHA `5040e51664bb53adc964c6135b5410c12804637c` for PR HEAD and synthetic merge**; GitHub compare HEAD..merge shows **zero changed files**. This is stronger than assuming the run's `head_sha` represents the checkout commit. The sealed Windows artifact identifies the tested synthetic merge SHA, legitimately not the PR head SHA.
- This proves the current no-conflict candidate's source tree equivalence only. A future RC merge, base movement or PR commit requires new post-integration evidence at its actual SHA.

## Completed hosted tests and build

All three workflows are **COMPLETED SUCCESS**, first attempt, associated with P7.6 PR HEAD `43b7eed`:

1. [Windows Build ARSAS #40427176051](https://github.com/masarray/arsas/actions/runs/40427176051). The exact synthetic merge was checked out, all 36 displayed steps including completion concluded SUCCESS. Windows .NET solution build and pinned engine/native checkout/verification passed. Log records **24/24 AR.Iec61850.Tests**, **2,987/2,987 ARSAS.Tests**, **6/6 packaged bridge managed tests**, and **9/9 native CTest**. Real portable EXE was published and smoke-tested. Canonical package sealing succeeded at source synthetic merge `b44df97`, trial engine pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f`, ArdIrec `7dbc7149db668126a493ce338264363a6ec5ec00`; package manifest says 594 files, 295,294,355 bytes uncompressed.
2. [CI P0 workflow integrity #40427176048](https://github.com/masarray/arsas/actions/runs/40427176048), PASS including **24/24 sanitized P7.6 unittest methods** (0.181 sec).
3. [CI P1 advisory #40427176092](https://github.com/masarray/arsas/actions/runs/40427176092), PASS.

The native bridge proof shows DLL SHA-256 `55a4818f45bb43438296c3cbd40e425b23fe21d892bf101f2c82e93a56353986`; explicitly does **not** retire independent COMTRADE validation.

## Six uploaded unexpired artifacts in the one Windows run

| Name | Artifact ID | Archive SHA-256 |
| --- | ---: | --- |
| ARSAS-1.6.42-win-x64-portable.exe | 12701165534 | b45e5f37bc1850ab7579912333489815c154a8351270b914ed841a83a2c7b7f0 |
| ARSAS-windows-package-input | 12701066038 | b8fbc50346785e46e7909e3f53add4908d2e8023edc77166ec85984c1d8551eb |
| ARSAS-native-ctest-shadow | 12700841491 | c004a434825d868f99e538476045c956bdaeb265b133c6fd9edf385b54d2469d |
| ARSAS-test-evidence | 12700148262 | 50e6855f99e836eb022af37f02b1b021f912f604e9296d0911f06f1d6b4fe7d8 |
| ARSAS-maintenance-dependency-inventory | 12699823392 | 4d42e06979e07b71b4c19e23481fe0bdb5add50c86b98e4daaaba260eb536b1d |
| ARSAS-source-snapshot | 12699733451 | 710aa656de49fbe3c61229e3e4d13bc75fc0862b9fcc3f0da05911860b6bfd30 |

These are GitHub Actions artifact archive digests, **not interchangeable with the inner portable EXE's own hash**. The `1.6.42` artifact name indicates source version, not a new stable release. Artifacts were unexpired at the time of inspection.

## Review and integration boundaries

- Both prior P2 inline code findings on #759 are FIXED, answered and marked RESOLVED. No `APPROVED` review has been observed; submitted reviews are COMMENTED only.
- The GitHub App cannot read protected branch configuration (HTTP 403); unknown required-check/ruleset conditions **must not** be silently assumed satisfied.
- #759 still has 4 changed files: `.github/workflows/ci-p0-workflow-integrity.yml`, `docs/audits/P7_6_FIELD_QUALIFICATION_INTAKE.md`, and the P7.6 parser/test scripts. D24 fix changed only three preexisting #759 paths. No changes to #757 CI gates, #753–#756 safety, #726/#716 Write, #688 SG7, #732 GUI or protected RC.
- Both ARSAS main `14e5dddfbaae012b61a021366c1a864d83d316f2` and ARIEC61850 main `3d21238d0b62f24faecd5948a4ee238c82b35966` remain unchanged, RC #727/#729 head `817c3910...` unchanged.
- Physical verification false, wire PCAP verification false: CI and local Copy Diagnostic string consistency are **NOT** independent live IED R7/R10 FAT. #446/#520 interoperability and SOE still open. #479 must stay OPEN until owner-approved #759 replacement is actually integrated. Trial engine does not obtain release authority from this evidence.

## CI budget integrity

- D24 review fix used only **one source commit/wave**. D25 performed read-only evidence inspection, no CI reruns.
- #757 docs-only gate qualification remains separate OPEN DRAFT. #542 investigation may leverage measured runner/time/log evidence, but keep independent COMTRADE Windows CMake/CTest until proof equivalence and approval; DO NOT turn one matching bridge DLL/CTest dataset into general deduplication permission.
- Windows run #40427176051 started 00:33:36Z and completed by 00:41:34Z (~7m58s wall time); this is not interchangeable with GitHub billable Windows runner minutes. The run's `run_attempt` is 1.

## D26 priorities

1. Obtain owner review/approval on #759, and owner-visible required-check configuration. No automatic merge just because three workflows are green.
2. If formally authorized, perform exact-head guarded integration into RC and rebuild/test actual resulting RC commit, preserving safety/engine authority. Then and only then assess closing historical #479 with individual evidence.
3. Continue #542 CI cost investigation using *actual* job/runner usage and independent native acceptance; do not modify #757 or COMTRADE checks without source-of-truth policy and hostile fixtures.
4. Maintain all physical/engine guards for eventual main/stable promotion, not merely source parity.
