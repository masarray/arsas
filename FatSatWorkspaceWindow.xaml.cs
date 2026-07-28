using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class FatSatWorkspaceWindow : Window, INotifyPropertyChanged
{
    private readonly FatSatWorkspaceService _workspaceService = new();
    private FatSatWorkspaceDocument _document = new();
    private FatSatTestCaseRow? _selectedRow;
    private string _statusText = "Create, open, or execute an evidence-backed FAT/SAT workspace.";
    private string? _currentPath;

    public FatSatWorkspaceWindow()
    {
        InitializeComponent();
        DataContext = this;
        ApplyDocument(_workspaceService.CreateDefault(), null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FatSatWorkspaceDocument Document
    {
        get => _document;
        private set
        {
            _document = value;
            Raise();
        }
    }

    public ObservableCollection<FatSatTestCaseRow> Rows { get; } = [];
    public IReadOnlyList<FatSatTestResult> ResultOptions { get; } = Enum.GetValues<FatSatTestResult>();

    public FatSatTestCaseRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value))
                return;
            _selectedRow = value;
            Raise();
        }
    }

    public string SummaryText
    {
        get
        {
            SynchronizeDocument();
            var summary = _workspaceService.Summarize(Document);
            return $"Total {summary.Total} · PASS {summary.Passed} · FAIL {summary.Failed} · REVIEW {summary.Review} · BLOCKED {summary.Blocked} · NOT RUN {summary.NotRun} · evidence {summary.EvidenceFiles}";
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            Raise();
        }
    }

    public string CurrentPathText => string.IsNullOrWhiteSpace(_currentPath)
        ? "Workspace has not been saved yet."
        : _currentPath;

    private void New_Click(object sender, RoutedEventArgs e)
        => ApplyDocument(_workspaceService.CreateDefault(), null);

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open ARSAS FAT/SAT workspace",
            Filter = "ARSAS FAT/SAT workspace (*.arsas-fat.json)|*.arsas-fat.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            StatusText = "Opening FAT/SAT workspace…";
            var document = await _workspaceService.OpenAsync(dialog.FileName);
            ApplyDocument(document, dialog.FileName);
            StatusText = $"Opened {Path.GetFileName(dialog.FileName)} with {Rows.Count} test case(s).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            StatusText = $"Workspace open failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "FAT/SAT Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save ARSAS FAT/SAT workspace",
                    Filter = "ARSAS FAT/SAT workspace (*.arsas-fat.json)|*.arsas-fat.json",
                    DefaultExt = ".arsas-fat.json",
                    AddExtension = true,
                    FileName = BuildDefaultWorkspaceFileName()
                };
                if (dialog.ShowDialog(this) != true)
                    return;
                _currentPath = dialog.FileName;
            }

            SynchronizeDocument();
            StatusText = "Saving FAT/SAT workspace atomically…";
            await _workspaceService.SaveAsync(_currentPath, Document);
            StatusText = $"Workspace saved: {Path.GetFileName(_currentPath)}.";
            Raise(nameof(CurrentPathText));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            StatusText = $"Workspace save failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "FAT/SAT Workspace", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export FAT/SAT audit package",
            Filter = "ARSAS FAT/SAT audit package (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = BuildDefaultAuditFileName()
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SynchronizeDocument();
            Document.UpdatedAtUtc = DateTimeOffset.UtcNow;
            StatusText = "Verifying evidence hashes and creating FAT/SAT audit package…";
            var result = await _workspaceService.ExportAuditPackageAsync(dialog.FileName, Document);
            StatusText = $"Audit package exported. SHA-256: {result.PackageSha256}.";
            MessageBox.Show(
                this,
                $"FAT/SAT audit package exported.\n\n{result.OutputPath}\n\nDisposition\n{(result.Summary.IsComplete ? "COMPLETE" : result.Summary.HasBlockingOutcome ? "REVIEW REQUIRED" : "INCOMPLETE")}\n\nSHA-256\n{result.PackageSha256}",
                "FAT/SAT Audit Package",
                MessageBoxButton.OK,
                result.Summary.IsComplete ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            StatusText = $"Audit package export failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "FAT/SAT Audit Package", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddTest_Click(object sender, RoutedEventArgs e)
    {
        var next = Rows.Count + 1;
        var model = new FatSatTestCase
        {
            Sequence = (next * 10).ToString("000"),
            Area = "Custom",
            Title = "New FAT/SAT test case",
            Procedure = "Describe the bounded execution steps.",
            ExpectedResult = "Describe the objective acceptance criterion."
        };
        var row = CreateRow(model);
        Rows.Add(row);
        SelectedRow = row;
        RefreshSummary("Custom test case added.");
    }

    private void RemoveTest_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
            return;
        var result = MessageBox.Show(
            this,
            $"Remove test case '{SelectedRow.Title}' and its evidence references?",
            "Remove FAT/SAT Test Case",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;
        var index = Rows.IndexOf(SelectedRow);
        Rows.Remove(SelectedRow);
        SelectedRow = Rows.ElementAtOrDefault(Math.Min(index, Math.Max(0, Rows.Count - 1)));
        RefreshSummary("Test case removed.");
    }

    private async void AttachEvidence_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
        {
            MessageBox.Show(this, "Select a test case before attaching evidence.", "FAT/SAT Evidence", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Attach FAT/SAT evidence",
            Filter = "Evidence files (*.zip;*.json;*.csv;*.txt;*.log;*.png;*.jpg;*.jpeg;*.pcap;*.pcapng;*.cfg;*.dat;*.cff)|*.zip;*.json;*.csv;*.txt;*.log;*.png;*.jpg;*.jpeg;*.pcap;*.pcapng;*.cfg;*.dat;*.cff|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            StatusText = "Hashing and attaching evidence…";
            foreach (var path in dialog.FileNames)
            {
                var evidence = await _workspaceService.CreateEvidenceReferenceAsync(path);
                if (SelectedRow.Evidence.Any(item => item.Sha256.Equals(evidence.Sha256, StringComparison.OrdinalIgnoreCase)))
                    continue;
                SelectedRow.Evidence.Add(evidence);
            }
            RefreshSummary($"Evidence attached to {SelectedRow.Sequence} · {SelectedRow.Title}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            StatusText = $"Evidence attachment failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "FAT/SAT Evidence", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveEvidence_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow?.SelectedEvidence is null)
            return;
        SelectedRow.Evidence.Remove(SelectedRow.SelectedEvidence);
        SelectedRow.SelectedEvidence = null;
        RefreshSummary("Evidence reference removed. The source file was not deleted.");
    }

    private void StampExecution_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
            return;
        if (string.IsNullOrWhiteSpace(SelectedRow.ExecutedBy))
            SelectedRow.ExecutedBy = Document.OperatorName;
        SelectedRow.ExecutedAtUtc = DateTimeOffset.UtcNow;
        RefreshSummary($"Execution timestamp recorded for {SelectedRow.Sequence}.");
    }

    private void TestGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => RefreshSummary("Test plan updated.")));

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ApplyDocument(FatSatWorkspaceDocument document, string? path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ApplyBuildProvenance(document);
        Document = document;
        _currentPath = path;
        Rows.Clear();
        foreach (var testCase in document.TestCases)
            Rows.Add(CreateRow(testCase));
        SelectedRow = Rows.FirstOrDefault();
        StatusText = $"Workspace ready with {Rows.Count} test case(s). Outcomes remain operator-owned and evidence-backed.";
        Raise(nameof(CurrentPathText));
        Raise(nameof(SummaryText));
    }

    private FatSatTestCaseRow CreateRow(FatSatTestCase model)
    {
        var row = new FatSatTestCaseRow(model);
        row.PropertyChanged += Row_PropertyChanged;
        row.Evidence.CollectionChanged += Evidence_CollectionChanged;
        return row;
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FatSatTestCaseRow.Result) or nameof(FatSatTestCaseRow.EvidenceCount))
            RefreshSummary("Test outcome updated.");
    }

    private void Evidence_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RefreshSummary("Evidence set updated.");

    private void RefreshSummary(string status)
    {
        SynchronizeDocument();
        StatusText = status;
        Raise(nameof(SummaryText));
    }

    private void SynchronizeDocument()
    {
        Document.TestCases = Rows.Select(row => row.Model).ToList();
        foreach (var row in Rows)
            row.SynchronizeEvidence();
    }

    private void ApplyBuildProvenance(FatSatWorkspaceDocument document)
    {
        var provenance = ReadBuildProvenance();
        document.ApplicationVersion = provenance.ApplicationVersion;
        document.ApplicationCommit = provenance.ApplicationCommit;
        document.EngineRepository = provenance.EngineRepository;
        document.EngineReference = provenance.EngineReference;
        document.EngineCommit = provenance.EngineCommit;
    }

    private static BuildProvenance ReadBuildProvenance()
    {
        var assembly = typeof(FatSatWorkspaceWindow).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                   ?? assembly.GetName().Version?.ToString()
                                   ?? "unknown";
        var plusIndex = informationalVersion.IndexOf('+');
        var version = plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
        var applicationCommit = plusIndex >= 0 ? informationalVersion[(plusIndex + 1)..] : "not-embedded";
        using var stream = assembly.GetManifestResourceStream("ARSAS.ARIEC61850.lock.json")
                           ?? throw new InvalidOperationException("Embedded ARIEC61850 provenance lock was not found.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return new BuildProvenance(
            version,
            applicationCommit,
            root.GetProperty("repository").GetString() ?? "unknown",
            root.GetProperty("ref").GetString() ?? "unknown",
            root.GetProperty("commit").GetString() ?? "unknown");
    }

    private string BuildDefaultWorkspaceFileName()
        => $"{SanitizeFileName(Document.ProjectName)}.arsas-fat.json";

    private string BuildDefaultAuditFileName()
        => $"ARSAS-FAT-SAT-{SanitizeFileName(Document.ProjectName)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}Z.zip";

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string((value ?? string.Empty).Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "workspace" : sanitized;
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed record BuildProvenance(
        string ApplicationVersion,
        string ApplicationCommit,
        string EngineRepository,
        string EngineReference,
        string EngineCommit);
}

public sealed class FatSatTestCaseRow : INotifyPropertyChanged
{
    private FatSatEvidenceReference? _selectedEvidence;

    public FatSatTestCaseRow(FatSatTestCase model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Evidence = new ObservableCollection<FatSatEvidenceReference>(model.Evidence);
        Evidence.CollectionChanged += (_, _) =>
        {
            SynchronizeEvidence();
            Raise(nameof(EvidenceCount));
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public FatSatTestCase Model { get; }
    public ObservableCollection<FatSatEvidenceReference> Evidence { get; }
    public int EvidenceCount => Evidence.Count;

    public FatSatEvidenceReference? SelectedEvidence
    {
        get => _selectedEvidence;
        set
        {
            _selectedEvidence = value;
            Raise();
        }
    }

    public string Sequence { get => Model.Sequence; set => SetText(Model.Sequence, value, assigned => Model.Sequence = assigned); }
    public string Area { get => Model.Area; set => SetText(Model.Area, value, assigned => Model.Area = assigned); }
    public string Title { get => Model.Title; set => SetText(Model.Title, value, assigned => Model.Title = assigned); }
    public string Procedure { get => Model.Procedure; set => SetText(Model.Procedure, value, assigned => Model.Procedure = assigned); }
    public string ExpectedResult { get => Model.ExpectedResult; set => SetText(Model.ExpectedResult, value, assigned => Model.ExpectedResult = assigned); }
    public string ActualResult { get => Model.ActualResult; set => SetText(Model.ActualResult, value, assigned => Model.ActualResult = assigned); }
    public string OperatorNote { get => Model.OperatorNote; set => SetText(Model.OperatorNote, value, assigned => Model.OperatorNote = assigned); }
    public string ExceptionOrDeviation { get => Model.ExceptionOrDeviation; set => SetText(Model.ExceptionOrDeviation, value, assigned => Model.ExceptionOrDeviation = assigned); }
    public string ExecutedBy { get => Model.ExecutedBy; set => SetText(Model.ExecutedBy, value, assigned => Model.ExecutedBy = assigned); }

    public DateTimeOffset? ExecutedAtUtc
    {
        get => Model.ExecutedAtUtc;
        set
        {
            if (Model.ExecutedAtUtc == value)
                return;
            Model.ExecutedAtUtc = value;
            Raise();
        }
    }

    public FatSatTestResult Result
    {
        get => Model.Result;
        set
        {
            if (Model.Result == value)
                return;
            Model.Result = value;
            if (value != FatSatTestResult.NotRun && Model.ExecutedAtUtc is null)
                Model.ExecutedAtUtc = DateTimeOffset.UtcNow;
            Raise();
            Raise(nameof(ExecutedAtUtc));
        }
    }

    public void SynchronizeEvidence()
        => Model.Evidence = Evidence.ToList();

    private void SetText(string current, string? value, Action<string> assign, [CallerMemberName] string? propertyName = null)
    {
        var normalized = value ?? string.Empty;
        if (string.Equals(current, normalized, StringComparison.Ordinal))
            return;
        assign(normalized);
        Raise(propertyName);
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
