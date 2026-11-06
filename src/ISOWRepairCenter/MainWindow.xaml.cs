using System.Windows;
using ISOWRepairCenter.ViewModels;

namespace ISOWRepairCenter;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    public MainWindow() => InitializeComponent();
    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e) => await ViewModel.RunDiagnosticsAsync();
    private async void RunSafeRepairs_Click(object sender, RoutedEventArgs e) => await ViewModel.RunSafeRepairsAsync();
    private async void RunExpertRepairs_Click(object sender, RoutedEventArgs e) => await ViewModel.RunExpertRepairsAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => ViewModel.Cancel();
    private void OpenSession_Click(object sender, RoutedEventArgs e) => ViewModel.OpenSessionFolder();
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(ViewModel.L["AboutText"], ViewModel.L["About"], MessageBoxButton.OK, MessageBoxImage.Information);
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (ViewModel.IsRunning && MessageBox.Show(ViewModel.L["CloseRunning"], ViewModel.L["WindowTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { e.Cancel=true; return; }
        if (ViewModel.IsRunning) ViewModel.Cancel();
        base.OnClosing(e);
    }
}
