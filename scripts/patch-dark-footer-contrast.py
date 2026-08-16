from pathlib import Path

patches = {
    Path("RcbExportFilterWindow.xaml"): [
        (
'''                <TextBlock Text="{Binding SelectionSummary}" FontSize="11.8" FontWeight="SemiBold"
                           Foreground="{StaticResource RcbInk}" TextTrimming="CharacterEllipsis"/>''',
'''                <TextBlock x:Name="FooterSelectionSummaryText" Text="{Binding SelectionSummary}" FontSize="11.8" FontWeight="SemiBold"
                           Foreground="#F4F8FC" TextTrimming="CharacterEllipsis"/>'''
        ),
        (
'''                <TextBlock Text="{Binding RemovalSummary}" Margin="12,0,0,0" FontSize="11.2"
                           Foreground="#315DBF" VerticalAlignment="Center"/>''',
'''                <TextBlock x:Name="FooterRemovalSummaryText" Text="{Binding RemovalSummary}" Margin="12,0,0,0" FontSize="11.2"
                           Foreground="#93C5FD" VerticalAlignment="Center"/>'''
        ),
    ],
    Path("SignalSelectionWizardWindow.xaml"): [
        (
'''                <TextBlock Text="{Binding SelectionCountText}"
                           FontSize="12.2"
                           FontWeight="SemiBold"
                           Foreground="#344054"
                           VerticalAlignment="Center"/>''',
'''                <TextBlock x:Name="FooterSelectionCountText"
                           Text="{Binding SelectionCountText}"
                           FontSize="12.2"
                           FontWeight="SemiBold"
                           Foreground="#F4F8FC"
                           VerticalAlignment="Center"/>'''
        ),
        (
'''                <Border Width="1" Height="16" Background="#D0D5DD" Margin="11,0"/>''',
'''                <Border x:Name="FooterSummarySeparator" Width="1" Height="16" Background="#8FA8BC" Margin="11,0"/>'''
        ),
        (
'''                <TextBlock Text="{Binding VisibleCountText}"
                           FontSize="12"
                           Foreground="#667085"
                           VerticalAlignment="Center"/>''',
'''                <TextBlock x:Name="FooterVisibleCountText"
                           Text="{Binding VisibleCountText}"
                           FontSize="12"
                           Foreground="#C9D8E5"
                           VerticalAlignment="Center"/>'''
        ),
    ],
}

for path, replacements in patches.items():
    text = path.read_text(encoding="utf-8")
    for old, new in replacements:
        count = text.count(old)
        if count != 1:
            raise SystemExit(f"Expected exactly one match in {path} but found {count}: {old[:100]!r}")
        text = text.replace(old, new, 1)
    path.write_text(text, encoding="utf-8", newline="\n")
    print(f"Patched {path}")
