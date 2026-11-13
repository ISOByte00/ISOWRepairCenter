using System.Windows;
using ISOWRepairCenter.ViewModels;

namespace ISOWRepairCenter;

public partial class MainWindow : Window
{
    private bool _closingAfterOperation;
    private bool _waitingForSafeClose;
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    public MainWindow() => InitializeComponent();
    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e) => await ViewModel.RunDiagnosticsAsync();
    private async void RunQuickDiagnostics_Click(object sender, RoutedEventArgs e) => await ViewModel.RunQuickDiagnosticsAsync();
    private async void RunSafeRepairs_Click(object sender, RoutedEventArgs e) => await ViewModel.RunSafeRepairsAsync();
    private async void RunExpertRepairs_Click(object sender, RoutedEventArgs e) => await ViewModel.RunExpertRepairsAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel.Cancel();
    private void OpenSession_Click(object sender, RoutedEventArgs e) => ViewModel.OpenSessionFolder();
    private async void RefreshSessionHistory_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshSessionHistoryAsync();
    private async void CompareSessionHistory_Click(object sender, RoutedEventArgs e) => await ViewModel.CompareSessionHistoryAsync();
    private void OpenHistoryReport_Click(object sender, RoutedEventArgs e) => ViewModel.OpenSelectedHistoryReport();
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(ViewModel.L["AboutText"], ViewModel.L["About"], MessageBoxButton.OK, MessageBoxImage.Information);
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_closingAfterOperation) { base.OnClosing(e); return; }
        if (!ViewModel.IsRunning) { base.OnClosing(e); return; }

        e.Cancel=true;
        if (_waitingForSafeClose) { base.OnClosing(e); return; }
        if (MessageBox.Show(ViewModel.L["CloseRunning"], ViewModel.L["WindowTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _waitingForSafeClose=true;
            ViewModel.Cancel();
            _ = CloseAfterOperationAsync();
        }
        base.OnClosing(e);
    }

    private async Task CloseAfterOperationAsync()
    {
        await ViewModel.WaitForIdleAsync();
        _closingAfterOperation=true;
        Close();
    }
}
