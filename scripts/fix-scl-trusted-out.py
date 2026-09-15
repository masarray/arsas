from pathlib import Path

scl = Path("Services/NativeIec61850Client.SclAssisted.cs")
text = scl.read_text(encoding="utf-8")
old = '''    internal bool TryGetTrustedSclDataSetDirectory(
        string dataSetReference,
        out ArMms.MmsDataSetDirectoryResult directory)
        => _trustedSclOnlineAuthorityActive &&
           _trustedSclDataSetDirectories.TryGetValue(
               NormalizeTrustedSclReference(dataSetReference),
               out directory!);
'''
new = '''    internal bool TryGetTrustedSclDataSetDirectory(
        string dataSetReference,
        out ArMms.MmsDataSetDirectoryResult directory)
    {
        directory = null!;
        if (!_trustedSclOnlineAuthorityActive)
            return false;

        return _trustedSclDataSetDirectories.TryGetValue(
            NormalizeTrustedSclReference(dataSetReference),
            out directory!);
    }
'''
if old not in text:
    raise SystemExit("Trusted SCL DataSet directory helper seam not found")
scl.write_text(text.replace(old, new, 1), encoding="utf-8")

adapter = Path("Services/NativeIec61850Client.TrustedSclStaticReporting.cs")
text = adapter.read_text(encoding="utf-8")
old = '''        plan.ReportControlReference = start.Session.ReportControl.Reference;
        plan.DataSetReference = start.Session.Plan.DataSetReference;
'''
new = '''        if (!string.IsNullOrWhiteSpace(start.Session.ReportControl.Reference))
            plan.ReportControlReference = start.Session.ReportControl.Reference;
        if (!string.IsNullOrWhiteSpace(start.Session.Plan.DataSetReference))
            plan.DataSetReference = start.Session.Plan.DataSetReference;
'''
if old not in text:
    raise SystemExit("Trusted SCL nullable assignment seam not found")
adapter.write_text(text.replace(old, new, 1), encoding="utf-8")
