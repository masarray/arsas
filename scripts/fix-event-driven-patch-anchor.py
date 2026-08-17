from pathlib import Path
p = Path('scripts/patch-event-driven-session-live-monitor.py')
s = p.read_text(encoding='utf-8')
old = "    '''                UpdateCommandFeedbackFromLivePoint(point);\\n''',\n    '''                UpdateCommandFeedbackFromLivePoint(point);\\n                ReconcileAnnunciatorFromLivePoint(point);\\n''')"
new = "    'UpdateCommandFeedbackFromLivePoint(point);',\n    'UpdateCommandFeedbackFromLivePoint(point);\\n            ReconcileAnnunciatorFromLivePoint(point);')"
if s.count(old) != 1:
    raise SystemExit(f'expected one helper anchor, got {s.count(old)}')
p.write_text(s.replace(old, new, 1), encoding='utf-8', newline='\n')
print('Made MainWindow point-flush anchor indentation-independent.')
