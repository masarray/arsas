# IEC 61850 Edition 2 prefixed-LN DPC trial — SG7 BCU

Source: SG7_BCU.iid plus ARSAS field diagnostics captured 2026-10-22. Never use this engineering note as physical permission to operate.

## Root cause

IEC LN names have prefix + lnClass + inst. The qualified engine (#184) classified Boolean Oper.ctlVal as SPC for `Bkr0XCBR2.Pos`, `Disc0XSWI1.Pos` and `BkrCSWI1.Pos`. Its semantic resolver recognized DPC Boolean ctlVal only where LN name began with CSWI/XCBR/XSWI; the configured prefixes were omitted from its parser. The SCL IID declares DPC Pos.stVal as Dbpos, ctlModel=sbo-with-enhanced-security, sboTimeout=30000 ms. ARSAS correctly refused conflicting DPC/SPC semantic authority, leaving 0/13 selected controls enabled.

## Correction (vendor-neutral)

Engine ARIEC61850 PR #189 parses the rightmost 4-character LN class immediately before the required trailing instance digits, retaining arbitrary LN prefixes (including prefix digits) and requiring the exact Pos DO. For Boolean DPC, the live descriptor also requires the same-object `.stVal` reference, control model, actual Oper and SBOw type proof, Cancel, and CommandTermination support. No arbitrary Boolean-to-DPC upgrade, no automatic Select/Operate, and no change to SBOw 400ms delayed LastApplError grace.

## Isolated fast validation

Production lock stays at engine #184 `9c7cb40364023a7b217e5ec31ab3105ba90142eb`. Use the existing CI-P1 reusable Windows build/test via manual FAST DEV, focus `command-read`, experimental engine SHA `3a09b1e52456fbef2fa2c5468c7cfbb9c027f7bc`. This compiles ARSAS against experimental engine and runs focused, read-only tests. The source manifest records the trial SHA and explicitly excludes this job from any FAT/release qualification. Engine PR #189 separately tests positive and negative semantic admission, including incorrect feedback and missing enhanced-control descriptor components.

## Existing evidence versus remaining acceptance

The SG7_BCU diagnostic already confirms ACSE/MMS association, six logical devices, 156 static DataSet members, 4/4 routed static RCBs, and 77/77 displayed values. It independently reports one BRCB BufOvfl=true and multiple invalid quality bits; preserve and highlight them rather than rewriting as Good. The field file does NOT prove an actual successful Select/SBOw, Operate, CommandTermination or breaker feedback caused by ARSAS. Operator-approved isolated FAT must record stage-specific command requests/responses, LastApplError, physical interlocks, final Pos.stVal, q, and timestamp before qualifying real control.

The official application/engine lock must not be changed solely because this trial's synthetic tests pass. A separate reviewed promotion with full regression and physical evidence is required.
