using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace ISOWRepairCenter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            StartupLog("Application startup started.");
            if (!IsAdministrator())
            {
                try
                {
                    var path = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
                    var startInfo = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" };
                    foreach (var argument in Environment.GetCommandLineArgs().Skip(1)) startInfo.ArgumentList.Add(argument);
                    Process.Start(startInfo);
                    StartupLog("Requested elevated restart.");
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show("Administrator permission is required to run Windows repair operations.", "ISOWRepairCenter", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StartupLog("Elevation request was cancelled or denied.");
                }
                Shutdown();
                return;
            }

            base.OnStartup(e);
            new MainWindow().Show();
            StartupLog("Main window opened.");
        }
        catch (Exception ex)
        {
            StartupLog(ex.ToString());
            MessageBox.Show($"ISOWRepairCenter could not start.\n\n{ex.Message}\n\nSee startup.log for details.", "Startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void StartupLog(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ISOWRepairCenter");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
