from pathlib import Path
import re


def replace_exact(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


app_path = Path("App.xaml")
main_path = Path("MainWindow.xaml")
app = app_path.read_text(encoding="utf-8")
main = main_path.read_text(encoding="utf-8")

if 'x:Key="CommandOpenGradient"' not in app:
    anchor = '''        <LinearGradientBrush x:Key="CommandHeaderSurface" StartPoint="0,0" EndPoint="1,0">
            <GradientStop Color="#EDF4FF" Offset="0"/>
            <GradientStop Color="#F7FAFF" Offset="1"/>
        </LinearGradientBrush>'''
    gradients = anchor + '''
        <LinearGradientBrush x:Key="CommandOpenGradient" StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="#1A9A62" Offset="0"/>
            <GradientStop Color="#087247" Offset="0.55"/>
            <GradientStop Color="#034B2E" Offset="1"/>
        </LinearGradientBrush>
        <LinearGradientBrush x:Key="CommandCloseGradient" StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="#EE5151" Offset="0"/>
            <GradientStop Color="#B6252D" Offset="0.55"/>
            <GradientStop Color="#75141D" Offset="1"/>
        </LinearGradientBrush>'''
    app = replace_exact(app, anchor, gradients, "command gradients")

command_styles = '''
        <Style x:Key="CommandActionButtonBase" TargetType="Button">
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="15,7"/>
            <Setter Property="MinWidth" Value="66"/>
            <Setter Property="MinHeight" Value="33"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Grid SnapsToDevicePixels="True">
                            <Border x:Name="Chrome"
                                    Background="{TemplateBinding Background}"
                                    BorderBrush="{TemplateBinding BorderBrush}"
                                    BorderThickness="{TemplateBinding BorderThickness}"
                                    CornerRadius="11">
                                <Border.Effect>
                                    <DropShadowEffect BlurRadius="11" ShadowDepth="3" Opacity="0.22" Color="#31445F"/>
                                </Border.Effect>
                                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"
                                                  Margin="{TemplateBinding Padding}"
                                                  TextElement.Foreground="{TemplateBinding Foreground}"/>
                            </Border>
                            <Border x:Name="InteractionSurface" Background="Transparent" CornerRadius="11"
                                    BorderBrush="Transparent" BorderThickness="1" IsHitTestVisible="False"/>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="InteractionSurface" Property="Background" Value="#30FFFFFF"/>
                                <Setter TargetName="InteractionSurface" Property="BorderBrush" Value="#8AFFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="Chrome" Property="Opacity" Value="0.80"/>
                                <Setter TargetName="InteractionSurface" Property="Background" Value="#18FFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="InteractionSurface" Property="BorderBrush" Value="White"/>
                                <Setter TargetName="InteractionSurface" Property="BorderThickness" Value="2"/>
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter TargetName="Chrome" Property="Opacity" Value="0.38"/>
                                <Setter Property="Cursor" Value="Arrow"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="CommandOpenButton" TargetType="Button" BasedOn="{StaticResource CommandActionButtonBase}">
            <Setter Property="Background" Value="{StaticResource CommandOpenGradient}"/>
            <Setter Property="BorderBrush" Value="#69D2A1"/>
        </Style>

        <Style x:Key="CommandCloseButton" TargetType="Button" BasedOn="{StaticResource CommandActionButtonBase}">
            <Setter Property="Background" Value="{StaticResource CommandCloseGradient}"/>
            <Setter Property="BorderBrush" Value="#FF9A9A"/>
        </Style>

        <Style x:Key="CommandConfirmButton" TargetType="Button" BasedOn="{StaticResource CommandActionButtonBase}">
            <Setter Property="Background" Value="{StaticResource CommandOpenGradient}"/>
            <Setter Property="BorderBrush" Value="#69D2A1"/>
            <Setter Property="MinWidth" Value="108"/>
            <Style.Triggers>
                <DataTrigger Binding="{Binding ControlPendingAction}" Value="Close">
                    <Setter Property="Background" Value="{StaticResource CommandCloseGradient}"/>
                    <Setter Property="BorderBrush" Value="#FF9A9A"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>

        <!-- The card surface remains visible through every IED action button. Only hover/press adds chrome. -->
        <Style x:Key="IedIconButton"'''

app, count = re.subn(
    r'\n        <Style x:Key="CommandOpenButton".*?\n        <Style x:Key="IedIconButton"',
    command_styles,
    app,
    count=1,
    flags=re.S,
)
if count != 1:
    raise RuntimeError(f"command style block: expected one match, found {count}")

ied_match = re.search(r'<Style x:Key="IedIconButton".*?</Style>', app, flags=re.S)
if not ied_match:
    raise RuntimeError("IedIconButton style not found")
ied = ied_match.group(0)
ied = replace_exact(ied, '<Setter Property="Background" Value="#F6F9FF"/>', '<Setter Property="Background" Value="Transparent"/>', "IED button background")
ied = replace_exact(ied, '<Setter Property="BorderBrush" Value="#D6E1F0"/>', '<Setter Property="BorderBrush" Value="Transparent"/>', "IED button border")
ied = replace_exact(ied,
    'BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" RenderTransformOrigin="0.5,0.5">\n                            <Border.RenderTransform><ScaleTransform ScaleX="1" ScaleY="1"/></Border.RenderTransform>',
    'BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">',
    "IED button transform")
ied = replace_exact(ied, '<Setter TargetName="Chrome" Property="Background" Value="#FFFFFF"/>', '<Setter TargetName="Chrome" Property="Background" Value="#CFFFFFFF"/>', "IED hover")
ied = replace_exact(ied, '<Setter TargetName="Chrome" Property="Background" Value="#E7F0FF"/>', '<Setter TargetName="Chrome" Property="Background" Value="#A6DCEBFF"/>', "IED pressed")
ied = replace_exact(ied, '<Setter TargetName="Chrome" Property="Opacity" Value="0.82"/>', '<Setter TargetName="Chrome" Property="Opacity" Value="0.84"/>', "IED pressed opacity")
app = app[:ied_match.start()] + ied + app[ied_match.end():]

main = replace_exact(main,
    '''            <Border x:Name="WorkflowNavShell" Width="720" Height="42" HorizontalAlignment="Left"
                    Background="#E7ECF5" CornerRadius="21" Padding="4" Effect="{StaticResource SoftShadow}">
                <Grid x:Name="WorkflowNavGrid" ClipToBounds="True">''',
    '''            <Border x:Name="WorkflowNavShell" Width="672" Height="56" HorizontalAlignment="Left"
                    Background="#E7ECF5" CornerRadius="20" Padding="5" Effect="{StaticResource SoftShadow}"
                    ClipToBounds="False">
                <Grid x:Name="WorkflowNavGrid" ClipToBounds="False">''',
    "navbar shell")
main = replace_exact(main, '<Grid MinHeight="72">', '<Grid MinHeight="70">', "IED card height")
main = replace_exact(main, '<ColumnDefinition Width="66"/>', '<ColumnDefinition Width="56"/>', "IED icon column")
main = replace_exact(main,
    '''                                                <!-- Large borderless relay / BCU icon; icon color is the connection state. -->
                                      <Grid Grid.Row="0" Grid.RowSpan="2" Grid.Column="0" Width="62" Height="62" Margin="0,0,4,0" VerticalAlignment="Center" Panel.ZIndex="2">
                                          <Viewbox Width="60" Height="60" HorizontalAlignment="Center" VerticalAlignment="Center">''',
    '''                                                <!-- Compact relay / BCU icon; the full SVG and glow stay inside the card. -->
                                      <Grid Grid.Row="0" Grid.RowSpan="2" Grid.Column="0" Width="52" Height="52" Margin="1,0,3,0"
                                            VerticalAlignment="Center" ClipToBounds="False" Panel.ZIndex="2">
                                          <Viewbox Width="44" Height="44" Stretch="Uniform"
                                                   HorizontalAlignment="Center" VerticalAlignment="Center">''',
    "IED relay icon host")
main = replace_exact(main,
    '<Path.Effect><DropShadowEffect BlurRadius="14" ShadowDepth="0" Opacity="0.58" Color="#EF4444"/></Path.Effect>',
    '<Path.Effect><DropShadowEffect BlurRadius="7" ShadowDepth="0" Opacity="0.38" Color="#EF4444"/></Path.Effect>',
    "IED disconnected glow")
main = replace_exact(main,
    '<Viewbox Width="16" Height="16"><Path Data="{StaticResource LucideSquare}" Style="{StaticResource LucideIcon}" Stroke="#DC2626"/></Viewbox>',
    '<Viewbox Width="16" Height="16"><Path Data="{StaticResource LucideSquare}" Style="{StaticResource LucideIcon}" Stroke="{StaticResource Warning}"/></Viewbox>',
    "Stop amber")
main = replace_exact(main,
    '<Viewbox Width="14" Height="14"><Path Data="{StaticResource LucideX}" Style="{StaticResource LucideIcon}" Stroke="#64748B"/></Viewbox>',
    '<Viewbox Width="14" Height="14"><Path Data="{StaticResource LucideX}" Style="{StaticResource LucideIcon}" Stroke="{StaticResource Danger}"/></Viewbox>',
    "Remove red")
main = replace_exact(main,
    '<Setter TargetName="RelayDeviceIcon" Property="Effect"><Setter.Value><DropShadowEffect BlurRadius="13" ShadowDepth="0" Opacity="0.48" Color="#16A34A"/></Setter.Value></Setter>',
    '<Setter TargetName="RelayDeviceIcon" Property="Effect"><Setter.Value><DropShadowEffect BlurRadius="8" ShadowDepth="0" Opacity="0.40" Color="#16A34A"/></Setter.Value></Setter>',
    "IED connected glow")

required_app = [
    'x:Key="CommandOpenGradient"',
    'x:Key="CommandCloseGradient"',
    'x:Key="CommandActionButtonBase"',
    '<Setter Property="Foreground" Value="White"/>',
    '<Style x:Key="IedIconButton" TargetType="Button">',
    '<Setter Property="Background" Value="Transparent"/>',
]
required_main = [
    'WorkflowNavShell" Width="672" Height="56"',
    '<ColumnDefinition Width="56"/>',
    '<Viewbox Width="44" Height="44" Stretch="Uniform"',
    'LucideSquare}" Style="{StaticResource LucideIcon}" Stroke="{StaticResource Warning}"',
    'LucideX}" Style="{StaticResource LucideIcon}" Stroke="{StaticResource Danger}"',
]
for token in required_app:
    if token not in app:
        raise RuntimeError(f"missing App.xaml invariant: {token}")
for token in required_main:
    if token not in main:
        raise RuntimeError(f"missing MainWindow.xaml invariant: {token}")

app_path.write_text(app, encoding="utf-8")
main_path.write_text(main, encoding="utf-8")
print("Applied direct XAML navbar, IED-card, icon-action, and command-action fixes.")
