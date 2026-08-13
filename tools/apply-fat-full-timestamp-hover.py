from pathlib import Path

path = Path('Models/IoTesting/IoTestModels.cs')
text = path.read_text(encoding='utf-8')
old = '''        var relay = evidence.IedTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "not supplied";
        var captured = evidence.CapturedAt.ToString("O", CultureInfo.InvariantCulture);
        return $"Relay timestamp: {relay}\\nARSAS capture: {captured}\\nQuality: {evidence.Quality}\\nSource: {evidence.AcquisitionSource}\\n{evidence.Verdict}: {evidence.VerdictReason}";
'''
new = '''        var displayed = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
            evidence.IedTimestamp,
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            "not supplied");
        var rawRelay = evidence.IedTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "not supplied";
        var captured = evidence.CapturedAt.ToString("O", CultureInfo.InvariantCulture);
        return $"Displayed (rounded to nearest ms): {displayed}\\n" +
               $"Raw IED timestamp (full precision): {rawRelay}\\n" +
               $"ARSAS capture (full precision): {captured}\\n" +
               $"Quality: {evidence.Quality}\\nSource: {evidence.AcquisitionSource}\\n" +
               $"{evidence.Verdict}: {evidence.VerdictReason}";
'''
if old not in text:
    raise SystemExit('Expected BuildEvidenceToolTip source block was not found; refusing fuzzy patch.')
path.write_text(text.replace(old, new, 1), encoding='utf-8')

# Self-clean: the temporary patcher/workflow must not remain in the product diff.
Path('tools/apply-fat-full-timestamp-hover.py').unlink(missing_ok=True)
Path('.github/workflows/apply-fat-full-timestamp-hover.yml').unlink(missing_ok=True)
