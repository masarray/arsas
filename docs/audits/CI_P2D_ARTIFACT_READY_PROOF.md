# CI-P2D — Artifact-ready canonical regression proof

Issue #438.

## Problem

P2A/P2B/P2C removed duplicate ARSAS compilation, but their guard runners still
waited until the entire canonical Build ARSAS workflow finished. In reference
run 37567436447, the exact regression artifact was uploaded at 03:37:44Z while
portable publish/smoke kept the canonical workflow alive until 03:39:45Z.
That left about 121 seconds of avoidable polling per consumer.

## P2D behavior

The canonical verifier keeps completed-workflow proof as its default. P2D adds
an explicit --allow-in-progress-artifact mode. In that mode, the newest matching
Build ARSAS run may satisfy the full-regression consumer while still in progress
only after the canonical manifest and TRX artifact exist and validate.

The verifier still requires exact PR head/branch, exact synthetic PR merge SHA,
exact pinned engine SHA, exact workflow run ID/attempt, safe bounded archive,
matching TRX SHA-256 and all-pass counters with zero failed/notExecuted tests.

A Build ARSAS run that is already completed with a non-success conclusion is
always rejected even when a prior test artifact exists. A missing artifact
continues bounded polling rather than becoming a pass.

## Authority boundary

Artifact-ready mode proves full regression only. If Build ARSAS is still
running, the proof explicitly reports workflowCompleted=false and
packagingSmokeProven=false. Portable publish/smoke remains the independent
Build ARSAS responsibility and is not promoted by Merge Execution, Mainline
Readiness or Production Route Guard.

## Consumers

- Smart Discovery Merge Execution Guard
- Smart Discovery Mainline Readiness
- Smart Discovery Production Promotion Guard

Each consumer opts in explicitly. No other caller changes from strict default
behavior.

## Measurement

The 121-second post-regression packaging interval is observational evidence,
not a flaky timing acceptance test. With three consumers, the reference run
represents roughly six runner-minutes that can be avoided per comparable PR.

## Rollback

Remove --allow-in-progress-artifact from the three consumers. This immediately
restores completed-workflow waiting while preserving the same canonical evidence
format, exact-SHA checks, physical authority and release policy.
