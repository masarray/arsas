using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AR.Iec61850.Scl.Export;

namespace ArIED61850Tester;

public sealed class SaveSclDialogViewModel : INotifyPropertyChanged
{
    private SclSchemaProfileDescriptor _selectedSchemaProfile;
    private string _editionHint = string.Empty;

    public SaveSclDialogViewModel(
        string iedName,
        string sourceDescription,
        SclSchemaProfile selectedProfile = SclSchemaProfile.Edition2V31)
    {
        IedName = string.IsNullOrWhiteSpace(iedName) ? "IED" : iedName.Trim();
        SourceDescription = string.IsNullOrWhiteSpace(sourceDescription)
            ? "IEC 61850 model"
            : sourceDescription.Trim();
        _selectedSchemaProfile = SclSchemaProfiles.Get(selectedProfile);
        UpdateHint();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string IedName { get; }
    public string SourceDescription { get; }

    public SclSchemaProfileDescriptor SelectedSchemaProfile
    {
        get => _selectedSchemaProfile;
        private set
        {
            if (_selectedSchemaProfile.Profile == value.Profile)
                return;
            _selectedSchemaProfile = value;
            OnPropertyChanged();
            UpdateHint();
        }
    }

    public string EditionHint
    {
        get => _editionHint;
        private set
        {
            if (string.Equals(_editionHint, value, StringComparison.Ordinal))
                return;
            _editionHint = value;
            OnPropertyChanged();
        }
    }

    public bool IsEdition2 => SelectedSchemaProfile.IsEdition2;

    public void Select(SclSchemaProfile profile)
    {
        SelectedSchemaProfile = SclSchemaProfiles.Get(profile);
        OnPropertyChanged(nameof(IsEdition2));
    }

    private void UpdateHint()
    {
        EditionHint = SelectedSchemaProfile.IsEdition2
            ? "Recommended for modern engineering exchange. The output is saved as an IID file."
            : "Use for legacy engineering systems that require Edition 1. The output is saved as an ICD file.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public partial class SaveSclWindow : Window
{
    private const double IndicatorOffset = 192d;

    public SaveSclWindow(
        string iedName,
        string sourceDescription,
        SclSchemaProfile selectedProfile = SclSchemaProfile.Edition2V31)
    {
        InitializeComponent();
        DataContext = new SaveSclDialogViewModel(iedName, sourceDescription, selectedProfile);
    }

    public SaveSclDialogViewModel ViewModel => (SaveSclDialogViewModel)DataContext;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyEditionVisuals(animate: false);
        DialogCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void Edition2_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Select(SclSchemaProfile.Edition2V31);
        ApplyEditionVisuals(animate: true);
    }

    private void Edition1_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Select(SclSchemaProfile.Edition1V16);
        ApplyEditionVisuals(animate: true);
    }

    private void ApplyEditionVisuals(bool animate)
    {
        var target = ViewModel.IsEdition2 ? 0d : IndicatorOffset;
        Edition2Button.Foreground = ViewModel.IsEdition2 ? Brushes.White : new SolidColorBrush(Color.FromRgb(52, 64, 84));
        Edition1Button.Foreground = ViewModel.IsEdition2 ? new SolidColorBrush(Color.FromRgb(52, 64, 84)) : Brushes.White;

        if (!animate)
        {
            EditionIndicatorTranslate.X = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(185),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        EditionIndicatorTranslate.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;
}
