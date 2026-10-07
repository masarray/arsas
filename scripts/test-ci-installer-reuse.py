#!/usr/bin/env python3
"""Offline fail-closed fixtures for CI-P3D validated installer reuse."""
from __future__ import annotations
import hashlib, importlib.util, io, json, tempfile, unittest, zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parent

def load(name, filename):
    s=importlib.util.spec_from_file_location(name, ROOT/filename); assert s and s.loader
    m=importlib.util.module_from_spec(s); s.loader.exec_module(m); return m
producer=load("installer_producer","write-ci-installer-manifest.py")
verifier=load("installer_verifier","verify-ci-installer-reuse.py")
S="a"*40; E="b"*40; A="c"*40; P="d"*64; RUN=777

def manifest(installer=b"installer"):
    return {
        "schemaVersion":1,"kind":"arsas-validated-windows-installer","version":"1.6.40",
        "sourceSha":S,"engineSha":E,"ardirecSha":A,"canonicalPackageRun":"4321/1",
        "canonicalPackageArtifactSha256":P,"installerWorkflow":"Validate ARSAS Windows installer",
        "workflowRunId":RUN,"runAttempt":1,"eventName":"push",
        "installerFile":"ARSAS-1.6.40-win-x64-setup.exe",
        "installerSha256":hashlib.sha256(installer).hexdigest(),"installerSizeBytes":len(installer),
        "installedSmokePassed":True,"releasePromotionAuthority":False}

def archive(m=None, installer=b"installer", extra=None):
    b=io.BytesIO()
    with zipfile.ZipFile(b,"w") as z:
        z.writestr("ARSAS-1.6.40-win-x64-setup.exe",installer)
        z.writestr("ci-installer-authority.json",json.dumps(m or manifest(installer)))
        z.writestr("SHA256SUMS.txt",b"fixture")
        for k,v in (extra or {}).items(): z.writestr(k,v)
    return b.getvalue()

class FakeApi:
    def __init__(self,status="completed",conclusion="success"):
        self.status=status; self.conclusion=conclusion; self.blob=archive()
    def get(self,url,*,binary=False):
        if binary:return self.blob
        if "installer-windows.yml/runs?" in url:
            return {"workflow_runs":[{"id":RUN,"run_attempt":1,"name":"Validate ARSAS Windows installer",
              "event":"push","head_branch":"main","head_sha":S,"status":self.status,"conclusion":self.conclusion}]}
        if "/artifacts" in url:
            return {"artifacts":[{"id":9,"name":"ARSAS-1.6.40-win-x64-installer","expired":False,
              "archive_download_url":"https://example.invalid/installer.zip"}]}
        raise RuntimeError(url)

class InstallerReuseTests(unittest.TestCase):
    def validate(self, blob=None, **kw):
        return verifier.validate_archive(blob or archive(),version="1.6.40",source_sha=S,engine_sha=E,
          ardirec_sha=A,package_artifact_sha256=P,workflow_run_id=RUN,run_attempt=1,**kw)
    def test_accepts_exact_installer(self):
        with tempfile.TemporaryDirectory() as t:
            p=self.validate(output_dir=Path(t))
            self.assertTrue((Path(t)/p["installerFile"]).is_file()); self.assertTrue(p["installedSmokePassed"])
    def test_rejects_tampered_installer(self):
        with self.assertRaisesRegex(verifier.InstallerProofError,"SHA-256|size"):
            self.validate(blob=archive(manifest(),b"tampered"))
    def test_rejects_wrong_package_digest(self):
        with self.assertRaisesRegex(verifier.InstallerProofError,"canonicalPackageArtifactSha256"):
            verifier.validate_archive(archive(),version="1.6.40",source_sha=S,engine_sha=E,ardirec_sha=A,
              package_artifact_sha256="e"*64,workflow_run_id=RUN,run_attempt=1)
    def test_rejects_release_authority_claim(self):
        m=manifest(); m["releasePromotionAuthority"]=True
        with self.assertRaisesRegex(verifier.InstallerProofError,"releasePromotionAuthority"): self.validate(blob=archive(m))
    def test_rejects_unsafe_path(self):
        with self.assertRaisesRegex(verifier.InstallerProofError,"unsafe"):
            self.validate(blob=archive(extra={"../escape":b"x"}))
    def test_latest_failed_run_is_hard_failure(self):
        with self.assertRaisesRegex(verifier.InstallerProofError,"failed"):
            verifier.verify(FakeApi(conclusion="failure"),repository="masarray/arsas",source_sha=S,version="1.6.40",
              engine_sha=E,ardirec_sha=A,package_artifact_sha256=P,wait_seconds=0,poll_seconds=1,output_dir=None)
    def test_release_workflow_promotes_validated_installer_and_keeps_manual_compile(self):
        w=(ROOT.parent/".github/workflows/release-windows.yml").read_text(encoding="utf-8")
        for token in ("Promote exact validated Windows installer for release","verify-ci-installer-reuse.py",
          "RELEASE_INSTALLER_AUTHORITY=validated-installer:","RELEASE_INSTALLER_SHA256",
          "if: github.event_name == 'workflow_dispatch'","build-windows-installer.ps1",
          "Smoke-test silent installer and uninstaller"):
            self.assertIn(token,w)

if __name__=="__main__": unittest.main()
