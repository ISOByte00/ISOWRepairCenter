using System.Collections.ObjectModel;
using System.Diagnostics;
using ISOWRepairCenter.Core;
using ISOWRepairCenter.Localization;

namespace ISOWRepairCenter.ViewModels;

/// <summary>Presentation layer for the read-only session-history API.</summary>
public sealed class SessionHistoryViewModel : ObservableObject
{
    private readonly string _sessionsRoot;
    private readonly SessionHistoryComparer _comparer;
    private readonly LocalizedText _text;
    private SessionHistorySessionItemViewModel? _earlierSession;
    private SessionHistorySessionItemViewModel? _laterSession;
    private SessionHistorySessionItemViewModel? _selectedSession;
    private string _status = string.Empty;
    private bool _isLoading;

    public ObservableCollection<SessionHistorySessionItemViewModel> Sessions { get; } = new();
    public ObservableCollection<SessionHistoryComparisonItemViewModel> ComparisonEntries { get; } = new();
    public ObservableCollection<SessionHistoryIssue> Issues { get; } = new();
    public string SessionsRoot => _sessionsRoot;
    public bool IsLoading { get => _isLoading; private set { if (Set(ref _isLoading, value)) OnPropertyChanged(nameof(CanCompare)); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool CanCompare => !IsLoading && EarlierSession is not null && LaterSession is not null && !ReferenceEquals(EarlierSession, LaterSession);
    public SessionHistorySessionItemViewModel? SelectedSession { get => _selectedSession; set => Set(ref _selectedSession, value); }
    public SessionHistorySessionItemViewModel? EarlierSession { get => _earlierSession; set { if (Set(ref _earlierSession, value)) OnPropertyChanged(nameof(CanCompare)); } }
    public SessionHistorySessionItemViewModel? LaterSession { get => _laterSession; set { if (Set(ref _laterSession, value)) OnPropertyChanged(nameof(CanCompare)); } }

    public SessionHistoryViewModel(LocalizedText text, string? sessionsRoot = null, SessionHistoryComparer? comparer = null)
    {
        _text = text;
        _sessionsRoot = sessionsRoot ?? new SessionStore().Root;
        _comparer = comparer ?? new SessionHistoryComparer();
        Status = _text["HistoryReady"];
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Status = _text["HistoryLoading"];
        ComparisonEntries.Clear(); Issues.Clear();
        try
        {
            var directories = await Task.Run(() => EnumerateDirectories(_sessionsRoot), cancellationToken);
            var loaded = new List<SessionHistorySessionItemViewModel>();
            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var session = await _comparer.ReadAsync(directory.Path, cancellationToken);
                loaded.Add(new SessionHistorySessionItemViewModel(directory.Path, directory.CreatedAt, session, _text));
            }
            Sessions.Clear();
            foreach (var item in loaded.OrderByDescending(x => x.CreatedAt)) Sessions.Add(item);
            SelectedSession = Sessions.FirstOrDefault();
            EarlierSession = Sessions.Count > 1 ? Sessions[1] : null;
            LaterSession = Sessions.FirstOrDefault();
            foreach (var issue in Sessions.SelectMany(x => x.Session.Issues)) Issues.Add(issue);
            Status = Sessions.Count switch { 0 => _text["HistoryEmpty"], 1 => _text["HistorySingle"], _ => string.Format(_text["HistoryLoaded"], Sessions.Count) };
        }
        catch (OperationCanceledException) { Status = _text["HistoryCancelled"]; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = string.Format(_text["HistoryLoadFailed"], ex.Message); }
        finally { IsLoading = false; }
    }

    public async Task CompareAsync(CancellationToken cancellationToken = default)
    {
        ComparisonEntries.Clear(); Issues.Clear();
        if (!CanCompare) { Status = _text["HistoryChooseTwo"]; return; }
        IsLoading = true;
        Status = _text["HistoryComparing"];
        try
        {
            // Re-read at comparison time so a session cannot become stale after listing.
            var comparison = await _comparer.CompareAsync(EarlierSession!.Path, LaterSession!.Path, cancellationToken);
            foreach (var entry in comparison.Entries) ComparisonEntries.Add(new SessionHistoryComparisonItemViewModel(entry, _text));
            foreach (var issue in comparison.Earlier.Issues.Concat(comparison.Later.Issues)) Issues.Add(issue);
            Status = ComparisonEntries.Count == 0 ? _text["HistoryNoDifferences"] : string.Format(_text["HistoryCompared"], ComparisonEntries.Count);
        }
        catch (OperationCanceledException) { Status = _text["HistoryCancelled"]; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = string.Format(_text["HistoryCompareFailed"], ex.Message); }
        finally { IsLoading = false; }
    }

    public void OpenSelectedReport()
    {
        var session = SelectedSession;
        if (session is null) { Status = _text["HistorySelectReport"]; return; }
        var report = new[] { "report.html", "report.txt" }.Select(x => Path.Combine(session.Path, x)).FirstOrDefault(File.Exists);
        if (report is null) { Status = _text["HistoryReportMissing"]; return; }
        try { Process.Start(new ProcessStartInfo(report) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException) { Status = string.Format(_text["HistoryOpenFailed"], ex.Message); }
    }

    public void RefreshLanguage()
    {
        foreach (var item in Sessions) item.RefreshLanguage();
        foreach (var item in ComparisonEntries) item.RefreshLanguage();
        OnPropertyChanged(nameof(Status));
    }

    private static IReadOnlyList<(string Path, DateTimeOffset CreatedAt)> EnumerateDirectories(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<(string, DateTimeOffset)>();
        return Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith("Session_", StringComparison.Ordinal))
            .Select(path => (path, ParseSessionDate(Path.GetFileName(path)) ?? new DateTimeOffset(Directory.GetCreationTimeUtc(path))))
            .OrderByDescending(x => x.Item2).ToArray();
    }

    private static DateTimeOffset? ParseSessionDate(string name)
    {
        const string prefix = "Session_";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length < prefix.Length + 15) return null;
        return DateTime.TryParseExact(name.Substring(prefix.Length, 15), "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.AssumeLocal, out var value) ? new DateTimeOffset(value) : null;
    }
}

public sealed class SessionHistorySessionItemViewModel : ObservableObject
{
    private readonly LocalizedText _text;
    public string Path { get; }
    public DateTimeOffset CreatedAt { get; }
    public SessionHistorySession Session { get; }
    public string DisplayName => $"{CreatedAt:G} · {System.IO.Path.GetFileName(Path)}";
    public string Metadata => string.Format(_text["HistoryMetadata"], Session.Findings.Count, Session.Operations.Count, Session.Issues.Count);
    public string Reliability => Session.IsReadable ? _text["HistoryReadable"] : _text["HistoryNeedsReview"];
    public SessionHistorySessionItemViewModel(string path, DateTimeOffset createdAt, SessionHistorySession session, LocalizedText text) => (Path, CreatedAt, Session, _text) = (path, createdAt, session, text);
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(Reliability));
    }
}

public sealed class SessionHistoryComparisonItemViewModel : ObservableObject
{
    private readonly LocalizedText _text;
    public SessionHistoryComparisonEntry Entry { get; }
    public string Finding => Entry.Later?.Code ?? Entry.Earlier?.Code ?? Entry.Key;
    public string OperationId => Entry.Later?.OperationId ?? Entry.Earlier?.OperationId ?? string.Empty;
    public string ChangeLabel => _text[$"HistoryChange{Entry.Change}"];
    public string Explanation => Entry.Explanation;
    public string Evidence => string.Format(_text["HistoryEvidence"], Describe(Entry.Earlier), Describe(Entry.Later));
    public SessionHistoryComparisonItemViewModel(SessionHistoryComparisonEntry entry, LocalizedText text) => (Entry, _text) = (entry, text);
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(ChangeLabel));
        OnPropertyChanged(nameof(Evidence));
    }
    private static string Describe(SessionHistoryFinding? finding) => finding is null ? "—" : $"{finding.Code}; {finding.Severity?.ToString() ?? "Unknown"}; {finding.Confidence?.ToString() ?? "Unknown"}";
}
