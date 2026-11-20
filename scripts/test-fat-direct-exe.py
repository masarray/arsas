#!/usr/bin/env python3
"""Offline regression for the manual FAT EXE publication guard."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("verify-fat-direct-exe.py")
spec = importlib.util.spec_from_file_location("fat_direct_exe", SCRIPT)
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)

SHA = "a" * 40


def run(name="Build ARSAS", sha=SHA, status="completed", conclusion="success"):
    return {"id": 123, "head_sha": sha, "name": name, "status": status,
            "conclusion": conclusion, "event": "pull_request"}


class FatDirectExeGuardTests(unittest.TestCase):
    def test_exact_qualified_build_and_installer(self):
        guard.validate_run(run(), SHA, "Build ARSAS")
        guard.validate_run(run("Validate ARSAS Windows installer"),
                           SHA, "Validate ARSAS Windows installer")

    def test_reject_incomplete_failed_stale_and_wrong_workflow(self):
        variants = [run(sha="b" * 40), run(status="in_progress"),
                    run(conclusion="failure"), run(name="Unrelated workflow")]
        for r in variants:
            with self.subTest(r=r), self.assertRaises(ValueError):
                guard.validate_run(r, SHA, "Build ARSAS")
        with self.assertRaises(ValueError):
            guard.validate_run(run(), "abc", "Build ARSAS")

    def test_check_pe_signature_and_sha(self):
        with tempfile.TemporaryDirectory() as directory:
            exe = Path(directory, "qualified.exe")
            data = bytearray(1024 * 1024 + 1)
            data[:2] = b"MZ"
            data[0x3C:0x40] = (0x80).to_bytes(4, "little")
            data[0x80:0x84] = b"PE\x00\x00"
            exe.write_bytes(data)
            import hashlib
            self.assertEqual(hashlib.sha256(data).hexdigest(), guard.check_exe(exe))
            data[0x80] = 0
            exe.write_bytes(data)
            with self.assertRaises(ValueError):
                guard.check_exe(exe)

    def test_reject_zip_disguised_as_exe(self):
        with tempfile.TemporaryDirectory() as directory:
            exe = Path(directory, "fake.exe")
            exe.write_bytes(b"PK\x03\x04" + b"\0" * (1024 * 1024))
            with self.assertRaises(ValueError):
                guard.check_exe(exe)


if __name__ == "__main__":
    unittest.main()
