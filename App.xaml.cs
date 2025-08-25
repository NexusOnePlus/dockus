using System.Windows;
using Velopack;

namespace dockus;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool createdNew;
        using (var mutex = new System.Threading.Mutex(true, "IconizerAppMutex", out createdNew))
        {
            if (!createdNew)
            {
                MessageBox.Show("App already running.", "Unique instance", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                VelopackApp.Build().Run();

                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fatal error on startup runtime: {ex.Message}", "Start failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

