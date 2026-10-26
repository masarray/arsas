# ARSAS ENGINEERING HANDOFF D23 → D24 — P7.6 source and CI qualified for owner review
**Date:** 2026-10-27 WIB. Continues [D22](ARSAS_RECONCILIATION_D22_2026-10-27.md). Source and evidence audit only; no runtime, physical-device, release, engine lock, protected RC or main mutation.

## D23 completed milestone — PR #759 ready for owner review (not merged)

[PR #759](https://github.com/masarray/arsas/pull/759) restores three files from historical [#479](https://github.com/masarray/arsas/pull/479) that are **missing** in protected integrated RC, with four changed paths total: read-only Python verifier, 20 sanitized test fixtures, engineer how-to and existing CI P0 workflow addition. Its Git SHA remains `fc8712c30b01480ef58cc59fe344bbb15dabb523`, base protected RC `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`, mergeable=true.

**The entire actual GitHub Actions suite has now completed SUCCESS at this exact commit, first attempt (no reruns):**
- [Build ARSAS 40419547617](https://github.com/masarray/arsas/actions/runs/40419547617) Windows job `121910257418` **SUCCESS**. Every step succeeded including pinned ARIEC/ArdIrec checkout, immutable checkout proof, .NET solution build, ARSAS + pinned-engine regressions, native CTest, exact-SHA canonical test attestation, native packaging, real portable EXE publish/smoke and sealed package verification.
- Six real GitHub Actions artifacts exist from that same run: `ARSAS-native-ctest-shadow`, `ARSAS-1.6.42-win-x64-portable.exe`, `ARSAS-windows-package-input`, `ARSAS-source-snapshot`, `ARSAS-maintenance-dependency-inventory`, `ARSAS-test-evidence`. Artifacts retain SHA-256 metadata and were all unexpired when queried. The name containing `1.6.42` denotes source version, not a newly approved release.
- [CI P0 Workflow Integrity 40419547619](https://github.com/masarray/arsas/actions/runs/40419547619) **SUCCESS**, including the new exact `Validate sanitized P7.6 dual-ingress diagnostic parser (read-only)` job step and `Ran 20 tests in 0.099s / OK`. All other CI P0 workflow/security/budget steps also success.
- [CI P1 Advisory 40419547848](https://github.com/masarray/arsas/actions/runs/40419547848) **SUCCESS**.

**D23 GitHub action:** reviewed source and the above tests/artifacts; documented code-only limitations and changed [#759](https://github.com/masarray/arsas/pull/759#issuecomment-6430085753) from OPEN DRAFT to **OPEN READY FOR REVIEW**. **No merge, auto-merge, engine repin, main/stable promotion or CI rerun.** Owner approval still necessary; GitHub App branch-protection/required-check endpoint 403 (cannot independently verify gate settings).

**No file-level ownership collision:** `list_pr_changed_filenames` comparison of the four #759 changed files against #757 CI-P4, #753–#756 safety stack, #726/#716 Write, #688 SG7 performance, and #732 IP GUI showed **zero identical changed paths** with each of them. This is a file-level audit, not assurance that integrated semantic/runtime gates are already accepted. #759 intentionally overlaps historical #479's three field-intake paths; #479 remains OPEN until successful owner-approved merger of replacement PR. Detailed coordination [#479](https://github.com/masarray/arsas/pull/479#issuecomment-6430097177).

## Safety / scope constraints and device acceptance
- Parser only qualifies locally saved **Copy Diagnostic consistency**, not true independent traffic; always records `physicalVerification=false` and `wirePcapVerification=false`. No MMS write/read, IED network, R10 PCAP, FAT or real-world claims.
- Exact IED, unique selected SCD AP, semantic fingerprints on Discovery/Open SCL, full <=8 per-ingress RCB target listing, routed BRCB and URCB signals, timezone first-route and unambiguous full report must all qualify. Input is bounded 8MiB, JSON output written atomically, a negative recheck replaces previous positive output with `NOT_ESTABLISHED`.
- #446 wider real-site interoperability, #520 buffered-RCB continuity/BufOvfl/EntryID and #606/#615 safety issues remain OPEN.
- Required checks/branch-protection owner visibility not provided to GitHub App (403). **Do not infer permission to merge from all-green Actions alone.**

## Protected integration and independent work streams
- ARSAS `main` remains `14e5dddfbaae012b61a021366c1a864d83d316f2`, ARIEC61850 `main` `3d21238d0b62f24faecd5948a4ee238c82b35966`; published stable `v1.6.42` unchanged.
- Integrated RC #727/#729 OPEN DRAFT at `817c3910e8223fa2ac531a4426ada5d2d4bbbe50`. RC trial engine pin `f75e323e9f8443aa20e0a3818a1d5d77255b2c0f` untouched.
- #757 P4 docs-only CI optimization OPEN DRAFT at `e0c9724b203ac38f2a947d71795ac1173ca5ed5b`, full green source CI plus prior real docs-only canary #758 CLOSED UNMERGED, not merged.
- Protected safety #753 `0652f66bed0c72dded20ff547e23e2ef1989341c`, #754 `2aab054b6cf9eb5500b1dce5ef5957eb8ba0232d`, #755 `7d235554225aa0341561b4e887a852fb6fd8e269`, #756 `098ec4abdf7ce8681ec97111f12f3f6151eade78`. Also #726/#716 Write, #688 SG7 perf and #732 GUI: no changes D23.
- D22 inventory 101 open PR / 16 independent open issues. D23 transitions #759 to ready without closing a PR or issue; total unchanged absent unrelated writers.

## Next D24 recommended execution
1. Seek explicit owner review and branch-protection/status approval for #759; **only then** perform an exact-head guarded merge into integrated RC. Do not merge automatically from this handoff, and do not mark historical #479 closed until real code is in RC.
2. Keep #757 separate; exact changed-file overlap zero permits sequential review, but final merge testing / required checks must still be evaluated in each actual integration order.
3. After any approved merged RC head change, rebuild exact new RC commit and validate source/test/artifact gate at new SHA; do not reuse an old branch SHA as post-merge evidence.
4. Continue investigating CI-cost #542: independent COMTRADE native CMake/CTest is intentionally mandatory despite 9/9 CTest name and identical DLL SHA in one observed case; fail-closed proof reuse needs independent evidence/policy before retiring Windows native job.
5. Respect #446/#520 hardware acceptance and safety / Write / SG7 / GUI owner changes; no unplanned main/stable promotion.

**D23 meaningful completion:** verified completed Windows build and all six real artifacts; code/test/CI-P0 review; graduated field validator #759 to review-ready; verified zero shared changed paths with protected parallel lanes; updated historical #479 evidence and handed off D24. No new CI cost triggered in D23.
