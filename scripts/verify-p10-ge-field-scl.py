#!/usr/bin/env python3
"""P10.4 GE Edition 1 SCL structural evidence gate (offline, read-only).

Not a physical IEC 61850 qualification: an operator must separately provide
association/PCAP, InformationReport, GI, continuity and semantic evidence.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from collections import Counter
from pathlib import Path
from xml.etree import ElementTree as ET

NS = "http://www.iec.ch/61850/2003/SCL"
TAG = lambda local: f"{{{NS}}}{local}"
MAX_BYTES = 16 * 1024 * 1024
HEX_SHA = re.compile(r"^[0-9a-fA-F]{64}$")


class RejectedEvidence(ValueError):
    """A malformed or unauthoritative field input."""


def read_bounded(path: Path) -> bytes:
    size = path.stat().st_size
    if size <= 0 or size > MAX_BYTES:
        raise RejectedEvidence(f"{path.name}: file size outside (0, {MAX_BYTES}] bytes")
    data = path.read_bytes()
    if len(data) != size:
        raise RejectedEvidence(f"{path.name}: file changed during read")
    return data


def scl_tree(data: bytes, label: str) -> ET.Element:
    # Do not allow entities/DTD or silently accept namespace-free pseudo SCL.
    if re.search(rb"<!\s*(?:DOCTYPE|ENTITY)\b", data, re.IGNORECASE):
        raise RejectedEvidence(f"{label}: DTD/ENTITY declaration is forbidden")
    try:
        root = ET.fromstring(data)
    except ET.ParseError as exc:
        raise RejectedEvidence(f"{label}: invalid XML: {exc}") from exc
    if root.tag != TAG("SCL"):
        raise RejectedEvidence(f"{label}: unexpected SCL root or namespace")
    return root


def only(items: list, label: str):
    if len(items) != 1:
        raise RejectedEvidence(f"{label}: expected exactly one, found {len(items)}")
    return items[0]


def attr(element: ET.Element, name: str) -> str:
    return (element.get(name) or "").strip()


def ied(root: ET.Element, name: str, label: str) -> ET.Element:
    candidates = [x for x in root.findall(TAG("IED")) if attr(x, "name") == name]
    return only(candidates, f"{label}: IED '{name}'")


def access_point(ied_node: ET.Element, name: str, label: str) -> ET.Element:
    return only([x for x in ied_node.findall(TAG("AccessPoint"))
                 if attr(x, "name") == name], f"{label}: AccessPoint '{name}'")


def endpoint(root: ET.Element, name: str, ap: str, label: str) -> str | None:
    comm = root.find(TAG("Communication"))
    if comm is None:
        return None
    matches = [node for node in comm.iter(TAG("ConnectedAP"))
               if attr(node, "iedName") == name and attr(node, "apName") == ap]
    if len(matches) > 1:
        raise RejectedEvidence(f"{label}: ambiguous ConnectedAP '{name}/{ap}'")
    if not matches:
        return None
    addr = matches[0].find(TAG("Address"))
    if addr is None:
        return None
    ips = [str(p.text or "").strip() for p in addr.findall(TAG("P"))
           if attr(p, "type").upper() == "IP"]
    if len(ips) > 1:
        raise RejectedEvidence(f"{label}: multiple IP entries")
    return ips[0] if ips else None


def snapshot(root: ET.Element, name: str, ap_name: str, label: str) -> dict:
    own_ied = ied(root, name, label)
    own_ap = access_point(own_ied, ap_name, label)
    server = only(own_ap.findall(TAG("Server")), f"{label}: Server")
    ld_by_inst = {}
    domains = {}
    logical_nodes = set()
    datasets = {}
    reports = []
    goose = []

    for ld in server.findall(TAG("LDevice")):
        inst = attr(ld, "inst")
        if not inst or inst in ld_by_inst:
            raise RejectedEvidence(f"{label}: missing/duplicate LDevice.inst '{inst}'")
        ld_by_inst[inst] = ld
        domain = attr(ld, "ldName") or name + inst
        if domain in domains:
            raise RejectedEvidence(f"{label}: duplicate resolved MMS domain '{domain}'")
        domains[domain] = inst
        for ln in list(ld):
            if ln.tag not in (TAG("LN0"), TAG("LN")):
                continue
            ln_class = attr(ln, "lnClass") or ("LLN0" if ln.tag == TAG("LN0") else "")
            prefix, ln_inst = attr(ln, "prefix"), attr(ln, "inst")
            node_key = (inst, prefix, ln_class, ln_inst)
            if not ln_class or node_key in logical_nodes:
                raise RejectedEvidence(f"{label}: invalid/duplicate logical node {node_key}")
            logical_nodes.add(node_key)
            for ds in ln.findall(TAG("DataSet")):
                ds_name = attr(ds, "name")
                key = (inst, prefix, ln_class, ln_inst, ds_name)
                if not ds_name or key in datasets:
                    raise RejectedEvidence(f"{label}: duplicate/empty DataSet {key}")
                members = []
                for fcda in ds.findall(TAG("FCDA")):
                    target_ld = attr(fcda, "ldInst") or inst
                    fc = attr(fcda, "fc")
                    do = attr(fcda, "doName")
                    ln_cl = attr(fcda, "lnClass")
                    if not target_ld or not ln_cl or not do or not fc:
                        raise RejectedEvidence(f"{label}: incomplete FCDA in {key}")
                    members.append((target_ld, attr(fcda, "prefix"), ln_cl,
                                    attr(fcda, "lnInst"), do,
                                    attr(fcda, "daName"), fc, attr(fcda, "ix")))
                if not members:
                    raise RejectedEvidence(f"{label}: empty DataSet {key}")
                datasets[key] = members
            for rc in ln.findall(TAG("ReportControl")):
                rc_name = attr(rc, "name")
                ds = attr(rc, "datSet")
                if not rc_name or not ds:
                    raise RejectedEvidence(f"{label}: ReportControl missing name/datSet")
                key = (inst, prefix, ln_class, ln_inst, ds)
                if key not in datasets:
                    raise RejectedEvidence(f"{label}: RCB '{rc_name}' binds missing DataSet {key}")
                reports.append((inst, prefix, ln_class, ln_inst, rc_name, ds,
                                attr(rc, "buffered"), attr(rc, "indexed")))
            for cb in ln.findall(TAG("GSEControl")):
                goose.append((inst, prefix, ln_class, ln_inst,
                              attr(cb, "name"), attr(cb, "datSet")))

    if not ld_by_inst:
        raise RejectedEvidence(f"{label}: missing LDevice")
    for key, members in datasets.items():
        for member in members:
            if member[0] not in ld_by_inst:
                raise RejectedEvidence(f"{label}: FCDA targets unknown LD '{member[0]}'")
            if member[:4] not in logical_nodes:
                raise RejectedEvidence(f"{label}: FCDA targets unknown LN {member[:4]}")
    return {
        "domains": domains,
        "logicalNodes": logical_nodes,
        "datasets": datasets,
        "reports": reports,
        "goose": goose,
        "endpoint": endpoint(root, name, ap_name, label),
    }


def readable_key(key: tuple) -> str:
    return "/".join(str(x) for x in key)


def compare(trusted: dict, saved: dict, domain: str, ip: str,
            expected_datasets: int, expected_members: int) -> dict:
    failures, review = [], []
    for label, snap in (("trusted", trusted), ("discovery export", saved)):
        if snap["domains"] != {domain: "F650"}:
            failures.append(f"{label}: MMS wire-domain→LD mapping differs from BCUGEF650→F650")
        if snap["endpoint"] is not None and snap["endpoint"] != ip:
            failures.append(f"{label}: SCL declared IP '{snap['endpoint']}' differs from '{ip}'")
        if snap["endpoint"] is None:
            review.append(f"{label}: no authoritative ConnectedAP IP; endpoint must be checked from field capture")
        if len(snap["datasets"]) != expected_datasets:
            failures.append(f"{label}: DataSets={len(snap['datasets'])}; expected {expected_datasets}")
        count = sum(len(m) for m in snap["datasets"].values())
        if count != expected_members:
            failures.append(f"{label}: FCDA members={count}; expected {expected_members}")

    if trusted["datasets"] != saved["datasets"]:
        missing = trusted["datasets"].keys() - saved["datasets"].keys()
        extra = saved["datasets"].keys() - trusted["datasets"].keys()
        changed = [key for key in trusted["datasets"].keys() & saved["datasets"].keys()
                   if trusted["datasets"][key] != saved["datasets"][key]]
        failures.append(f"DataSet FCDA ordered semantic mismatch: missing={len(missing)}, "
                        f"extra={len(extra)}, changed={len(changed)}")
        review.extend("DataSet mismatch: " + readable_key(key)
                      for key in list(sorted(missing | extra | set(changed)))[:12])

    # Different design/offline vs discoverable live scope is NOT made invisible;
    # this is a separate engineering review, not a DataSet/reporting pass.
    missing_ln = trusted["logicalNodes"] - saved["logicalNodes"]
    added_ln = saved["logicalNodes"] - trusted["logicalNodes"]
    if missing_ln or added_ln:
        review.append(f"LN design/live scope differs: trusted-only={len(missing_ln)}, "
                      f"export-only={len(added_ln)}; DO/DA semantic parity NOT VERIFIED")
    if Counter(trusted["reports"]) != Counter(saved["reports"]):
        review.append("Logical ReportControl configuration differs from trusted IID; "
                      "RCB semantic parity and buffer continuity NOT VERIFIED")
    if Counter(trusted["goose"]) != Counter(saved["goose"]):
        review.append("GOOSE control set differs; GOOSE address/cb reference parity NOT VERIFIED")

    return {"structuralVerdict": "FAIL" if failures else "STRUCTURE_PASS_ONLY",
            "failures": failures, "review": review,
            "dataSets": len(saved["datasets"]),
            "staticMembers": sum(len(m) for m in saved["datasets"].values()),
            "logicalNodes": {"trusted": len(trusted["logicalNodes"]),
                             "discoveryExport": len(saved["logicalNodes"])},
            "logicalReportControls": {"trusted": len(trusted["reports"]),
                                      "discoveryExport": len(saved["reports"])}}


def assess(trusted_file: Path, generated_file: Path, source_sha: str,
           domain: str, ip: str, ap_name: str, datasets: int = 8,
           members: int = 124) -> dict:
    if not HEX_SHA.fullmatch(source_sha):
        raise RejectedEvidence("trusted source SHA-256 must be exactly 64 hex characters")
    if trusted_file.resolve() == generated_file.resolve():
        raise RejectedEvidence("source IID and Discovery-export SCL cannot be the same file")
    origin = read_bounded(trusted_file)
    actual = hashlib.sha256(origin).hexdigest()
    if actual.lower() != source_sha.lower():
        raise RejectedEvidence("trusted engineering IID SHA-256 mismatch; fail closed")
    output = read_bounded(generated_file)
    t = snapshot(scl_tree(origin, "trusted IID"), "BCUGE", ap_name, "trusted IID")
    g = snapshot(scl_tree(output, "Discovery SCL"), "BCUGE", ap_name, "Discovery SCL")
    result = compare(t, g, domain, ip, datasets, members)
    return {
        "schema": "arsas-p10-ge-field-check-v1",
        "source": {"sha256": actual, "bytes": len(origin)},
        "discoveryExport": {"sha256": hashlib.sha256(output).hexdigest(), "bytes": len(output)},
        "expectedEndpoint": f"{ip}:102",
        "observedDomainClaim": domain,
        "identity": "BCUGE",
        "logicalDevice": "F650",
        **result,
        "unverifiedPhysicalGates": [
            "actual MMS association/device provenance and raw domain name-list",
            "67/67 runtime points and six concrete static RCBs with InformationReport traffic",
            "zero cyclic MMS process-value polling and valid GI causality",
            "LN/DO/DA semantic closure, 6400 name-list cap and incomplete chains",
            "BRCB BufOvfl/sequence/EntryID replay continuity across sessions",
            "GOOSE address and wire subscriber semantics",
        ],
        "physicalQualification": "NOT_VERIFIED",
        "r10PromotionAllowed": False,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trusted-iid", type=Path, required=True)
    parser.add_argument("--trusted-sha256", required=True)
    parser.add_argument("--discovery-scl", type=Path, required=True)
    parser.add_argument("--observed-mms-domain", required=True)
    parser.add_argument("--endpoint-ip", default="192.16.1.33")
    parser.add_argument("--access-point", required=True)
    parser.add_argument("--out", type=Path, help="Optional JSON report path")
    args = parser.parse_args(argv)
    try:
        result = assess(args.trusted_iid, args.discovery_scl,
                        args.trusted_sha256, args.observed_mms_domain,
                        args.endpoint_ip, args.access_point)
        code = 2 if result["structuralVerdict"] == "FAIL" else 0
    except (OSError, RejectedEvidence) as exc:
        result = {"schema": "arsas-p10-ge-field-check-v1",
                  "structuralVerdict": "REJECTED_INPUT",
                  "reason": str(exc), "physicalQualification": "NOT_VERIFIED",
                  "r10PromotionAllowed": False}
        code = 2
    report = json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True)
    print(report)
    if args.out is not None:
        try:
            args.out.write_text(report + "\n", encoding="utf-8")
        except OSError as exc:
            print(f"Could not write report: {exc}", file=sys.stderr)
            return 2
    return code


if __name__ == "__main__":
    sys.exit(main())
