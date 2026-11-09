using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ISOWRepairCenter.Core;
using ISOWRepairCenter.Localization;

namespace ISOWRepairCenter.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SessionStore _store = new();
    private readonly ProcessRunner _runner = new();
    private readonly ConcurrentQueue<string> _pendingLog = new();
    private readonly DispatcherTimer _logTimer;
    private const int MaxLiveLogCharacters = 200_000;
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource? _runCompletion;
    private string _status = string.Empty;
    private string _liveLog = string.Empty;
    private string _dismSource = string.Empty;
    private string _windowsSource = string.Empty;
    private string _efiTarget = string.Empty;
    private bool _isRunning;
    private string _lastSession = string.Empty;
    private string _selectedLanguage = "tr";

    public ObservableCollection<OperationViewModel> Diagnostics { get; }
    public ObservableCollection<OperationViewModel> SafeRepairs { get; }
    public ObservableCollection<OperationViewModel> ExpertRepairs { get; }
    public ObservableCollection<DiagnosticFindingViewModel> Findings { get; } = new();
    public LocalizedText L { get; } = new();
    public IReadOnlyList<LanguageOption> Languages { get; } = new[] { new LanguageOption("tr", "Türkçe"), new LanguageOption("en", "English") };
    public string SelectedLanguage { get => _selectedLanguage; set { if (Set(ref _selectedLanguage, value)) SetLanguage(value); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string LiveLog { get => _liveLog; private set => Set(ref _liveLog, value); }
    public string DismSource { get => _dismSource; set => Set(ref _dismSource, value); }
    public string WindowsSource { get => _windowsSource; set => Set(ref _windowsSource, value); }
    public string EfiTarget { get => _efiTarget; set => Set(ref _efiTarget, value); }
    public bool IsRunning { get => _isRunning; private set { if(Set(ref _isRunning, value)) { OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanCancel)); } } }
    public bool CanStart => !IsRunning;
    public bool CanCancel => IsRunning;
    public string LastSession { get => _lastSession; private set => Set(ref _lastSession, value); }
    public string AdministratorStatus => App.IsAdministrator() ? L["AdminEnabled"] : L["AdminUnavailable"];
    public string WindowsStatus => Environment.OSVersion.VersionString;

    public MainViewModel()
    {
        var all = OperationCatalog.Create().Select(x => new OperationViewModel(x)).ToList();
        Diagnostics = new(all.Where(x => x.Operation.Category == OperationCategory.Diagnostic));
        SafeRepairs = new(all.Where(x => x.Operation.Category == OperationCategory.SafeRepair));
        ExpertRepairs = new(all.Where(x => x.Operation.Category == OperationCategory.ExpertRepair));
        _logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(125) };
        _logTimer.Tick += (_, _) => FlushLog();
        _logTimer.Start();
        SetLanguage(_selectedLanguage);
    }

    public Task RunDiagnosticsAsync() => RunSelectedAsync(Diagnostics);
    public Task RunSafeRepairsAsync() => RunSelectedAsync(SafeRepairs);
    public Task RunExpertRepairsAsync() => RunSelectedAsync(ExpertRepairs);
    public void Cancel() => _cancellation?.Cancel();
    public Task WaitForIdleAsync() => _runCompletion?.Task ?? Task.CompletedTask;
    public void OpenSessionFolder()
    {
        if (!string.IsNullOrWhiteSpace(_store.SessionDirectory) && Directory.Exists(_store.SessionDirectory)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.SessionDirectory}\"") { UseShellExecute=true });
    }

    private async Task RunSelectedAsync(IEnumerable<OperationViewModel> source)
    {
        var selected = source.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) { MessageBox.Show(L["SelectOperation"], L["WindowTitle"], MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var inputs = new OperationInputs { DismSource=DismSource.Trim(), WindowsSource=WindowsSource.Trim(), EfiTarget=EfiTarget.Trim() };
        var error = selected.Select(x => x.Operation.Validate?.Invoke(inputs)).FirstOrDefault(x => x is not null);
        if (error is not null) { MessageBox.Show(error, L["RequiredInput"], MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var isExpert = selected.Any(x => x.Operation.Category == OperationCategory.ExpertRepair);
        if (!Confirm(selected, isExpert)) return;

        IsRunning=true; LiveLog=string.Empty; _cancellation=new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runCompletion=completion;
        var executor = new RepairExecutor(_runner, _store); var restorePoints = new RestorePointService(_runner); var results=new List<OperationResult>();
        var stoppedBeforeExecution = false;
        try
        {
            _store.Start();
            var riskSelected = selected.Where(x => x.Operation.RequiresRestorePoint).ToList();
            if (riskSelected.Count > 0)
            {
                WriteLog(L["RestoreStart"]);
                var restore = await restorePoints.TryCreateAsync(_store.StepOutputPath(0, "restore-point"), WriteLog, _cancellation.Token);
                if (restore.ExitCode != 0 && !restore.Cancelled)
                {
                    var proceed = MessageBox.Show(L["RestoreUnavailable"], L["RestoreTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (proceed != MessageBoxResult.Yes) { stoppedBeforeExecution=true; return; }
                }
                if (restore.Cancelled) return;
            }
            var number=0;
            foreach (var item in selected)
            {
                number++; Status=$"{L["Running"]} {number}/{selected.Count}: {item.Title}"; WriteLog($"\n[{DateTime.Now:T}] {item.Title}");
                var result=await executor.ExecuteAsync(item.Operation, inputs, number, WriteLog, _cancellation.Token); results.Add(result);
                if (result.Cancelled) break;
            }
        }
        catch (Exception ex) { WriteLog($"Application error: {ex}"); }
        finally
        {
            var findings = DiagnosticAnalyzer.Analyze(results);
            Findings.Clear();
            foreach (var finding in findings) Findings.Add(new DiagnosticFindingViewModel(finding, _selectedLanguage));
            try
            {
                if (!string.IsNullOrWhiteSpace(_store.SessionDirectory))
                {
                    await _store.SaveAsync(results, findings);
                    LastSession=$"{L["LastSession"]}: {_store.SessionDirectory}";
                }
            }
            catch (Exception ex) { WriteLog($"Report saving failed: {ex.Message}"); }
            finally
            {
                FlushLog();
                Status=stoppedBeforeExecution || results.Any(x => x.Cancelled) ? L["Cancelled"] : results.Count > 0 && results.All(x => x.Succeeded) ? L["Completed"] : L["CompletedWarnings"];
                IsRunning=false; _cancellation?.Dispose(); _cancellation=null;
                completion.TrySetResult();
                if (ReferenceEquals(_runCompletion, completion)) _runCompletion=null;
            }
        }
    }

    private bool Confirm(IReadOnlyList<OperationViewModel> selected, bool expert)
    {
        var commands = string.Join(Environment.NewLine, selected.SelectMany(x => x.Operation.BuildSteps(new OperationInputs { DismSource=DismSource, WindowsSource=WindowsSource, EfiTarget=EfiTarget })).Select(x => x.Preview));
        if (expert && MessageBox.Show(L["ExpertConfirm"], L["ExpertTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return false;
        return MessageBox.Show($"{L["Preview"]}\n\n{commands}\n\n{L["Continue"]}", L["PreviewTitle"], MessageBoxButton.YesNo, expert ? MessageBoxImage.Warning : MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void WriteLog(string line) => _pendingLog.Enqueue(line);

    private void FlushLog()
    {
        if (_pendingLog.IsEmpty) return;
        var batch = new System.Text.StringBuilder();
        while (_pendingLog.TryDequeue(out var line)) batch.AppendLine(line);
        var next = LiveLog + batch;
        LiveLog = next.Length <= MaxLiveLogCharacters ? next : next[^MaxLiveLogCharacters..];
    }

    private void SetLanguage(string language)
    {
        L.Language=language;
        Status=L["Ready"]; LastSession=L["NoSession"];
        OnPropertyChanged(nameof(AdministratorStatus));
        foreach (var item in Diagnostics.Concat(SafeRepairs).Concat(ExpertRepairs)) item.SetLanguage(language);
        foreach (var finding in Findings) finding.SetLanguage(language);
    }
}

public sealed record LanguageOption(string Code, string Name);
