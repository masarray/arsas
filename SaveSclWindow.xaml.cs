using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using AR.Iec61850.Scl.Export;

namespace ArIED61850Tester;

public sealed class SaveSclDialogViewModel : INotifyPropertyChanged
{
    private readonly bool _legacySasCidMode;
    private SclSchemaProfileDescriptor _selectedSchemaProfile;
    private string _editionHint = string.Empty;

    public SaveSclDialogViewModel(
        string iedName,
        string sourceDescription,
        SclSchemaProfile selectedProfile = SclSchemaProfile.Edition2V31,
        bool legacySasCidMode = false)
    {
        IedName = string.IsNullOrWhiteSpace(iedName) ? "IED" : iedName.Trim();
        SourceDescription = string.IsNullOrWhiteSpace(sourceDescription)
            ? "IEC 61850 model"
            : sourceDescription.Trim();
        _legacySasCidMode = legacySasCidMode;
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
        if (_legacySasCidMode)
        {
            EditionHint = SelectedSchemaProfile.IsEdition2
                ? "Use when the target SAS accepts Edition 2. This selected-RCB workflow still saves a CID file."
                : "Recommended for existing legacy SAS systems. The selected-RCB output is saved as an Edition 1 CID file.";
            return;
        }

        EditionHint = SelectedSchemaProfile.IsEdition2
            ? "Recommended for modern engineering exchange. The output is saved as an IID file."
            : "Use for legacy engineering systems that require Edition 1. The output is saved as an ICD file.";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public partial class SaveSclWindow : Window
{
    public SaveSclWindow(
        string iedName,
        string sourceDescription,
        SclSchemaProfile selectedProfile = SclSchemaProfile.Edition2V31,
        bool legacySasCidMode = false)
    {
        InitializeComponent();
        DataContext = new SaveSclDialogViewModel(iedName, sourceDescription, selectedProfile, legacySasCidMode);
    }

    public SaveSclDialogViewModel ViewModel => (SaveSclDialogViewModel)DataContext;

    private void Window_Loaded(object sender, RoutedEventArgs e)
        => ApplyEditionVisuals();

    private void Edition2_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Select(SclSchemaProfile.Edition2V31);
        ApplyEditionVisuals();
    }

    private void Edition1_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Select(SclSchemaProfile.Edition1V16);
        ApplyEditionVisuals();
    }

    private void ApplyEditionVisuals()
    {
        Edition2Toggle.IsChecked = ViewModel.IsEdition2;
        Edition1Toggle.IsChecked = !ViewModel.IsEdition2;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;
}
