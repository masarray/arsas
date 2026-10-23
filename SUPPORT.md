# ARSAS Support

## Before opening an issue

1. Confirm the target IP address, subnet, route, and TCP port 102 reachability.
2. Confirm the intended network adapter and disable unintended competing routes during laboratory troubleshooting where appropriate.
3. Confirm another client is not consuming the IED's available association, report, control, or file-service resources.
4. Build the current ARSAS branch against the required ARIEC61850 engine revision.
5. Reproduce the problem with the smallest possible synthetic project, fixture, or sanitized SCL file.
6. Open **Diagnostics → Copy Diagnostic** and review the report before sharing it.
7. For GOOSE or Sampled Values, confirm the approved capture adapter, VLAN visibility, switch mirroring, offload behavior, and Npcap policy.
8. For file transfer, confirm the exact remote path, service response, local destination, and whether the problem is listing, opening, reading, closing, or saving.

## Engineering issue information

Include:

- ARSAS version or commit;
- ARIEC61850 version, branch, or commit;
- Windows version and .NET SDK/runtime version;
- connection method: direct IP, saved project, or SCL import;
- affected workflow: discovery, reporting, monitoring, SOE, GOOSE, Sampled Values, file transfer, SCL, control, packaging, installer, or UI;
- exact expected and observed behavior;
- sanitized diagnostic report;
- whether the issue is deterministic;
- whether it reproduces with a unit test, deterministic fixture, simulator, loopback, or authorized laboratory IED;
- relevant scale information such as IED count, selected-signal count, DataSet size, stream count, sample rate, or file size.

## Sanitization requirements

Do not attach customer, employer, or station-confidential material. Remove or replace:

- station, bay, feeder, IED, and project names;
- credentials, tokens, certificates, and private keys;
- restricted IP addressing, VLAN data, MAC addresses, and network topology;
- serial numbers and asset identifiers;
- relay settings, disturbance records, raw captures, and proprietary configuration;
- proprietary screenshots, manuals, support responses, or configuration exports;
- personal information.

Synthetic examples are preferred.

## Community and commercial support

The GPL community edition is available to everyone under its license. Public issues are appropriate for reproducible bugs, documentation problems, bounded feature proposals, and interoperability evidence that can be shared safely.

Separate commercial arrangements may cover project-specific engineering, proprietary integration rights, OEM or white-label distribution, training, validation planning, compatibility work, on-site or remote commissioning support, and priority response commitments.

See [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md). Commercial rights and service commitments require a separate written agreement.

## Operational boundary

Support guidance does not authorize command operation, switching, packet capture, file retrieval, report configuration, or connection to an operational network. The site owner, switching authority, cybersecurity owner, test lead, and applicable procedures determine whether an active test is permitted.
