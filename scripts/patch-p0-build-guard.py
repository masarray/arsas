from pathlib import Path

path = Path('.github/workflows/build.yml')
text = path.read_text(encoding='utf-8')
replacements = [
    ('WorkflowNavShell" Width="760" Height="56"', 'WorkflowNavShell" Width="900" Height="56"'),
    ("$sasUi -notmatch 'shell.Width = 760'", "$sasUi -notmatch 'shell.Width = 900'"),
]
for old, new in replacements:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'Expected exactly one match for {old!r}, found {count}')
    text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8', newline='\n')
print('Updated build visual invariants to the six-tab P0 navbar contract.')
