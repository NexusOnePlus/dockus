using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Velopack;
using Velopack.Sources;

namespace dockus.Settings;

/// <summary>
/// Lógica de interacción para SettingsWindow.xaml
/// </summary>
public partial class SettingsWindow : Window
{


    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;



    public SettingsWindow()
    {
        InitializeComponent();
        this.DataContext = dockus.Core.Models.AppSettings.Current;
        Loaded += OnLoaded;
        DisplayVersion();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int useDark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

        ChangelogText.Text = "Loading changelog...";
        ChangelogText.Text = await FetchChangelogAsync();
    }

    public void DisplayVersion()
    {
        var token = "";
        var source = new Velopack.Sources.GithubSource("https://github.com/NexusOnePlus/dockus", token, true);
        var updateManager = new UpdateManager(source);
        string version;
        if (updateManager.IsInstalled)
        {
            version = updateManager.CurrentVersion?.ToString() ?? "Unknown";
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Debug";
        }
        VersionText.Text = $"v{version}";
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var token = "";
        var source = new Velopack.Sources.GithubSource("https://github.com/NexusOnePlus/dockus", token, true);
        var updateManager = new UpdateManager(source);
        try
        {
            UpdateButton.IsEnabled = false;
            UpdateButtonText.Text = "Checking...";

            if (!updateManager.IsInstalled)
            {
                MessageBox.Show("Updates can only be checked in an installed version of the application.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var newVersion = await updateManager.CheckForUpdatesAsync();
                if (newVersion == null)
                {
                    MessageBox.Show("Your application is up to date.", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                UpdateButtonText.Text = "Downloading...";
                try
                {
                    await updateManager.DownloadUpdatesAsync(newVersion);
                }
                catch (Exception exDownload)
                {
                    MessageBox.Show($"Error during download:\n{exDownload}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    updateManager.ApplyUpdatesAndRestart(newVersion);
                }
                catch (Exception exApply)
                {
                    MessageBox.Show($"Error applying update:\n{exApply}", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception exCheck)
            {
                MessageBox.Show($"Error checking for updates:\n{exCheck}", "Check Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"General error in update process:\n{ex}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
            UpdateButtonText.Text = "Check for Updates";
        }
    }

    private async Task<string> FetchChangelogAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("dockus-client");

            var json = await http.GetStringAsync("https://api.github.com/repos/NexusOnePlus/dockus/releases");
            var releases = JsonSerializer.Deserialize<GitHubRelease[]>(json);

            if (releases != null && releases.Length > 0)
            {
                return releases[0].body ?? "No changelog available.";
            }

            return "No releases found.";
        }
        catch (Exception ex)
        {
            return $"Error fetching changelog: {ex.Message}";
        }
    }
    public class GitHubRelease
    {
        public string? body { get; set; }
    }

}
