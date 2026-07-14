# Support

## Before opening an issue

1. Confirm the target IP address, subnet, route, and TCP port 102 reachability.
2. Confirm the intended network adapter and disable unintended competing routes during laboratory troubleshooting when appropriate.
3. Confirm another client is not consuming the IED's available association or report resources.
4. Build the current ArIED branch against the required ARIEC61850 engine revision or branch.
5. Reproduce the problem with the smallest possible synthetic project or sanitized SCL file.
6. Open **Diagnostics → Copy Diagnostic** and review the report before sharing it.

## Engineering issue template

Include:

- ArIED version or commit;
- ARIEC61850 version, branch, or commit;
- Windows version and .NET SDK/runtime version;
- connection method: direct IP, saved project, or SCL import;
- affected workflow: discovery, reporting, monitoring, events, control, packaging, or UI;
- exact expected and observed behavior;
- sanitized diagnostic report;
- whether the issue is deterministic;
- whether it reproduces with the simulator or a synthetic model.

## Sanitization requirements

Do not attach customer or employer confidential material. Remove or replace:

- station, bay, feeder, IED, and project names;
- credentials, tokens, certificates, and private keys;
- restricted IP addressing and network topology;
- serial numbers and asset identifiers;
- proprietary screenshots, manuals, support responses, or configuration exports;
- personal information.

Synthetic examples are preferred.

## Commercial engineering support

The GPL community edition is available to everyone under its license. Separate commercial arrangements may cover project-specific engineering, proprietary integration rights, OEM or white-label distribution, training, validation planning, compatibility work, and priority support.

See [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md). The notice describes available discussion areas; commercial rights and service commitments require a separate written agreement.

## Operational boundary

Support guidance does not authorize command operation, switching, or connection to an operational network. The site owner, switching authority, test lead, and applicable procedures determine whether an active test is permitted.
