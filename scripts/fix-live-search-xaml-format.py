from pathlib import Path
p = Path('MainWindow.xaml')
s = p.read_text(encoding='utf-8')
old = 'Text="{Binding SelectedDevice.Points.Count, StringFormat= {0} signals}"'
new = 'Text="{Binding SelectedDevice.Points.Count, StringFormat={}{0} signals}"'
if s.count(old) != 1:
    raise SystemExit(f'expected one match, got {s.count(old)}')
p.write_text(s.replace(old, new, 1), encoding='utf-8', newline='\n')
print('Escaped WPF Binding.StringFormat literal braces.')
