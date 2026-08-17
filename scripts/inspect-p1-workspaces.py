from pathlib import Path

path = Path('MainWindow.xaml')
text = path.read_text(encoding='utf-8')
markers = [
    ('EVENT', '<!-- EVENT LOG -->', '<!-- EVENT-LATCHED ALARM ANNUNCIATOR -->'),
    ('ALARM', '<!-- EVENT-LATCHED ALARM ANNUNCIATOR -->', '<!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->'),
    ('GOOSE', '<!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->', '<!-- DIAGNOSTICS -->'),
    ('DIAGNOSTICS', '<!-- DIAGNOSTICS -->', '</TabControl>'),
]
for label, start, end in markers:
    a = text.find(start)
    b = text.find(end, a + len(start))
    if a < 0 or b < 0:
        raise SystemExit(f'{label}: markers not found')
    section = text[a:b]
    print(f'===== {label} ({section.count(chr(10))+1} lines) =====')
    print(section)
    print(f'===== END {label} =====')
