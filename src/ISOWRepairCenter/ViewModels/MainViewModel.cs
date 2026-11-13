using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using ISOWRepairCenter.Core;
using ISOWRepairCenter.Localization;

namespace ISOWRepairCenter.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SessionStore _store = new();
    private readonly ProcessRunner _runner = new();
    private readonly UserPreferencesStore _preferences = new();
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
    private string _diagnosticSearchText = string.Empty;

    public ObservableCollection<OperationViewModel> Diagnostics { get; }
    public ICollectionView FilteredDiagnostics { get; }
    public ObservableCollection<OperationViewModel> SafeRepairs { get; }
    public ObservableCollection<OperationViewModel> ExpertRepairs { get; }
    public ObservableCollection<DiagnosticFindingViewModel> Findings { get; } = new();
    public LocalizedText L { get; } = new();
    public SessionHistoryViewModel SessionHistory { get; }
    public IReadOnlyList<LanguageOption> Languages { get; } = new[] { new LanguageOption("tr", "Türkçe"), new LanguageOption("en", "English") };
    public string SelectedLanguage { get => _selectedLanguage; set { if (Set(ref _selectedLanguage, value)) { _preferences.SaveLanguage(value); SetLanguage(value); } } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string LiveLog { get => _liveLog; private set => Set(ref _liveLog, value); }
    public string DismSource { get => _dismSource; set => Set(ref _dismSource, value); }
    public string WindowsSource { get => _windowsSource; set => Set(ref _windowsSource, value); }
    public string EfiTarget { get => _efiTarget; set => Set(ref _efiTarget, value); }
    public string DiagnosticSearchText
    {
        get => _diagnosticSearchText;
        set
        {
            if (Set(ref _diagnosticSearchText, value)) FilteredDiagnostics.Refresh();
        }
    }
    public string SelectedDiagnosticsText => string.Format(L["SelectedDiagnostics"], Diagnostics.Count(x => x.IsSelected));
    public string FindingSummary
    {
        get
        {
            if (Findings.Count == 0) return L["NoFindings"];
            var critical = Findings.Count(x => x.Severity == FindingSeverity.Critical);
            var warnings = Findings.Count(x => x.Severity == FindingSeverity.Warning);
            var review = Findings.Count(x => x.Finding.Confidence != FindingConfidence.Known);
            var informational = Findings.Count(x => x.Severity == FindingSeverity.Information && x.Finding.Confidence == FindingConfidence.Known);
            return string.Format(L["FindingSummary"], Findings.Count, critical, warnings, informational, review);
        }
    }
    public bool IsRunning { get => _isRunning; private set { if(Set(ref _isRunning, value)) { OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanCancel)); } } }
    public bool CanStart => !IsRunning;
    public bool CanCancel => IsRunning;
    public string LastSession { get => _lastSession; private set => Set(ref _lastSession, value); }
    public string QuickDiagnosticsLabel => _selectedLanguage == "tr" ? "Hızlı tanılamayı çalıştır" : "Run quick diagnostics";
    public string AdministratorStatus => App.IsAdministrator() ? L["AdminEnabled"] : L["AdminUnavailable"];
    public string WindowsStatus => WindowsVersionInfo.Current().DisplayName;

    public MainViewModel()
    {
        _selectedLanguage = _preferences.LoadLanguage();
        var all = OperationCatalog.Create().Select(x => new OperationViewModel(x)).ToList();
        Diagnostics = new(all.Where(x => x.Operation.Category == OperationCategory.Diagnostic));
        foreach (var diagnostic in Diagnostics) diagnostic.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(OperationViewModel.IsSelected)) OnPropertyChanged(nameof(SelectedDiagnosticsText)); };
        FilteredDiagnostics = CollectionViewSource.GetDefaultView(Diagnostics);
        FilteredDiagnostics.Filter = MatchesDiagnosticSearch;
        SafeRepairs = new(all.Where(x => x.Operation.Category == OperationCategory.SafeRepair));
        ExpertRepairs = new(all.Where(x => x.Operation.Category == OperationCategory.ExpertRepair));
        _logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(125) };
        _logTimer.Tick += (_, _) => FlushLog();
        _logTimer.Start();
        SessionHistory = new SessionHistoryViewModel(L, _store.Root);
        SetLanguage(_selectedLanguage);
    }

    public Task RunDiagnosticsAsync() => RunSelectedAsync(Diagnostics);
    public Task RunQuickDiagnosticsAsync() => RunOperationsAsync(Diagnostics.Where(x => DiagnosticProfiles.IsQuickDiagnostic(x.Operation)).ToList());
    public Task RunSafeRepairsAsync() => RunSelectedAsync(SafeRepairs);
    public Task RunExpertRepairsAsync() => RunSelectedAsync(ExpertRepairs);
    public void Cancel() => _cancellation?.Cancel();
    public Task WaitForIdleAsync() => _runCompletion?.Task ?? Task.CompletedTask;
    public void OpenSessionFolder()
    {
        if (!string.IsNullOrWhiteSpace(_store.SessionDirectory) && Directory.Exists(_store.SessionDirectory)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.SessionDirectory}\"") { UseShellExecute=true });
    }
    public Task RefreshSessionHistoryAsync() => SessionHistory.RefreshAsync();
    public Task CompareSessionHistoryAsync() => SessionHistory.CompareAsync();
    public void OpenSelectedHistoryReport() => SessionHistory.OpenSelectedReport();

    private Task RunSelectedAsync(IEnumerable<OperationViewModel> source) => RunOperationsAsync(source.Where(x => x.IsSelected).ToList());

    private bool MatchesDiagnosticSearch(object item)
    {
        if (item is not OperationViewModel operation || string.IsNullOrWhiteSpace(DiagnosticSearchText)) return true;
        var query = DiagnosticSearchText.Trim();
        return operation.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || operation.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || operation.Operation.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || operation.Operation.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunOperationsAsync(IReadOnlyList<OperationViewModel> selected)
    {
        if (selected.Count == 0) { MessageBox.Show(L["SelectOperation"], L["WindowTitle"], MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var inputs = new OperationInputs { DismSource=DismSource.Trim(), WindowsSource=WindowsSource.Trim(), EfiTarget=EfiTarget.Trim() };
        var error = selected.Select(x => x.Operation.Validate?.Invoke(inputs)).FirstOrDefault(x => x is not null);
        if (error is not null) { MessageBox.Show(error, L["RequiredInput"], MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var isExpert = selected.Any(x => x.Operation.Category == OperationCategory.ExpertRepair);
        if (!Confirm(selected, isExpert)) return;

        IsRunning=true; LiveLog=string.Empty; _cancellation=new CancellationTokenSource();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runCompletion=completion;
        var executor = new RepairExecutor(_runner, _store); var baselineCapture = new PreRepairBaselineCapture(_runner, _store); var verifier = new PostRepairVerifier(_runner, _store); var restorePoints = new RestorePointService(_runner); var results=new List<OperationResult>(); var baselines = new List<RepairBaselineResult>(); var verifications = new List<RepairVerificationResult>(); StepResult? restorePoint = null;
        var stoppedBeforeExecution = false;
        var baselineCancelled = false;
        var repairCancelled = false;
        var verificationCancelled = false;
        try
        {
            _store.Start();
            var riskSelected = selected.Where(x => x.Operation.RequiresRestorePoint).ToList();
            if (riskSelected.Count > 0)
            {
                WriteLog(L["RestoreStart"]);
                restorePoint = await restorePoints.TryCreateAsync(_store.StepOutputPath(0, "restore-point"), WriteLog, _cancellation.Token);
                if (restorePoint.ExitCode != 0 && !restorePoint.Cancelled)
                {
                    var proceed = MessageBox.Show(L["RestoreUnavailable"], L["RestoreTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (proceed != MessageBoxResult.Yes) { stoppedBeforeExecution=true; return; }
                }
                if (restorePoint.Cancelled) { stoppedBeforeExecution=true; return; }
            }
            var number=0;
            foreach (var item in selected)
            {
                RepairBaselineResult? baseline = null;
                if (RepairVerificationCatalog.For(item.Operation.Id).Count > 0)
                {
                    Status = L["CapturingBaseline"];
                    WriteLog($"\n[{DateTime.Now:T}] {L["CapturingBaseline"]}: {item.Title}");
                    try
                    {
                        baseline = await baselineCapture.CaptureAsync(item.Operation, number + 1, WriteLog, _cancellation.Token);
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Baseline capture failed: {ex.Message}");
                        baseline = new RepairBaselineResult(item.Operation.Id, item.Operation.Title, BaselineOutcome.Unknown, DateTimeOffset.Now, DateTimeOffset.Now, Array.Empty<OperationResult>(), Array.Empty<DiagnosticFinding>());
                    }
                    baselines.Add(baseline);
                    if (baseline.Outcome == BaselineOutcome.Cancelled) { baselineCancelled = true; break; }
                }
                number++; Status=$"{L["Running"]} {number}/{selected.Count}: {item.Title}"; WriteLog($"\n[{DateTime.Now:T}] {item.Title}");
                var result=await executor.ExecuteAsync(item.Operation, inputs, number, WriteLog, _cancellation.Token); results.Add(result);
                if (result.Cancelled) { repairCancelled = true; break; }
                if (result.Succeeded && (item.Operation.Category is OperationCategory.SafeRepair or OperationCategory.ExpertRepair) && RepairVerificationCatalog.For(item.Operation.Id).Count > 0)
                {
                    if (item.Operation.RequiresRestart)
                    {
                        var deferred = new RepairVerificationResult(item.Operation.Id, item.Operation.Title, VerificationOutcome.Deferred, DateTimeOffset.Now, DateTimeOffset.Now, Array.Empty<OperationResult>(), Array.Empty<DiagnosticFinding>(), PostRepairVerifier.SummaryFor(VerificationOutcome.Deferred));
                        verifications.Add(RepairChangeAnalyzer.Apply(baseline, deferred));
                        continue;
                    }
                    Status = L["RunningVerification"];
                    WriteLog($"\n[{DateTime.Now:T}] {L["RunningVerification"]}: {item.Title}");
                    RepairVerificationResult verification;
                    try
                    {
                        verification = await verifier.VerifyAsync(item.Operation, number, WriteLog, _cancellation.Token);
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Post-repair verification failed: {ex.Message}");
                        verification = new RepairVerificationResult(item.Operation.Id, item.Operation.Title, VerificationOutcome.Unknown, DateTimeOffset.Now, DateTimeOffset.Now, Array.Empty<OperationResult>(), Array.Empty<DiagnosticFinding>(), "Post-repair verification encountered an error before a conclusion was reached.");
                    }
                    verification = RepairChangeAnalyzer.Apply(baseline, verification);
                    verifications.Add(verification);
                    if (verification.Outcome == VerificationOutcome.Cancelled) { verificationCancelled = true; break; }
                }
            }
        }
        catch (Exception ex) { WriteLog($"Application error: {ex}"); }
        finally
        {
            var findings = DiagnosticAnalyzer.Analyze(results).Concat(verifications.SelectMany(x => x.Findings)).ToArray();
            Findings.Clear();
            foreach (var finding in findings) Findings.Add(new DiagnosticFindingViewModel(finding, _selectedLanguage));
            OnPropertyChanged(nameof(FindingSummary));
            try
            {
                if (!string.IsNullOrWhiteSpace(_store.SessionDirectory))
                {
                    await _store.SaveAsync(results, findings, verifications, baselines, restorePoint);
                    LastSession=$"{L["LastSession"]}: {_store.SessionDirectory}";
                }
            }
            catch (Exception ex) { WriteLog($"Report saving failed: {ex.Message}"); }
            finally
            {
                FlushLog();
                Status = stoppedBeforeExecution || baselineCancelled ? L["BaselineCancelled"]
                    : repairCancelled ? L["Cancelled"]
                    : verificationCancelled ? L["VerificationCancelled"]
                    : results.Count > 0 && results.All(x => x.Succeeded) && verifications.All(x => x.Outcome is VerificationOutcome.Verified or VerificationOutcome.NotApplicable) ? L["Completed"]
                    : L["CompletedWarnings"];
                IsRunning=false; _cancellation?.Dispose(); _cancellation=null;
                completion.TrySetResult();
                if (ReferenceEquals(_runCompletion, completion)) _runCompletion=null;
            }
        }
    }

    private bool Confirm(IReadOnlyList<OperationViewModel> selected, bool expert)
    {
        var inputs = new OperationInputs { DismSource=DismSource, WindowsSource=WindowsSource, EfiTarget=EfiTarget };
        var baselineCommands = selected.SelectMany(x => RepairVerificationCatalog.For(x.Operation.Id)).SelectMany(x => x.Steps).Select(x => x.Preview).ToArray();
        var repairCommands = selected.SelectMany(x => x.Operation.BuildSteps(inputs)).Select(x => x.Preview).ToArray();
        var verificationCommands = selected.SelectMany(x => RepairVerificationCatalog.For(x.Operation.Id)).SelectMany(x => x.Steps).Select(x => x.Preview).ToArray();
        var commands = baselineCommands.Length > 0 ? $"{L["PreRepairBaseline"]}\n{string.Join(Environment.NewLine, baselineCommands)}\n\n" : string.Empty;
        commands += string.Join(Environment.NewLine, repairCommands);
        if (verificationCommands.Length > 0) commands += $"\n\n{L["PostRepairVerification"]}\n" + string.Join(Environment.NewLine, verificationCommands);
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
        OnPropertyChanged(nameof(SelectedDiagnosticsText));
        OnPropertyChanged(nameof(FindingSummary));
        OnPropertyChanged(nameof(AdministratorStatus));
        OnPropertyChanged(nameof(QuickDiagnosticsLabel));
        foreach (var item in Diagnostics.Concat(SafeRepairs).Concat(ExpertRepairs)) item.SetLanguage(language);
        FilteredDiagnostics.Refresh();
        foreach (var finding in Findings) finding.SetLanguage(language);
        SessionHistory.RefreshLanguage();
    }
}

public sealed record LanguageOption(string Code, string Name);
