from pathlib import Path
p = Path('MainWindow.CommandPanelUx.cs')
s = p.read_text(encoding='utf-8')
old = 'return (liveArmed || testMode) && supportsOperate && !busy && !AlreadyActive(command, current);'
new = 'return (liveArmed || testMode) && supportsOperate && !busy && (testMode || !AlreadyActive(command, current));'
if s.count(old) != 1:
    raise RuntimeError(f'expected one command enabled expression, found {s.count(old)}')
p.write_bytes(s.replace(old, new, 1).replace('\r\n', '\n').replace('\n', '\r\n').encode('utf-8'))
