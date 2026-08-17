from pathlib import Path
p = Path('scripts/patch-event-driven-session-live-monitor.py')
s = p.read_text(encoding='utf-8')
start_marker = "main_cs = 'MainWindow.xaml.cs'"
end_marker = "# 2) Live Monitor header"
start = s.index(start_marker)
end = s.index(end_marker)
replacement = '''main_cs = 'MainWindow.xaml.cs'\nreplace_once(\n    main_cs,\n    'UpdateCommandFeedbackFromLivePoint(point);',\n    'UpdateCommandFeedbackFromLivePoint(point);\\n            ReconcileAnnunciatorFromLivePoint(point);')\n\n'''
p.write_text(s[:start] + replacement + s[end:], encoding='utf-8', newline='\n')
print('Made MainWindow point-flush anchor indentation-independent.')
