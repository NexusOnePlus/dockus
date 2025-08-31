using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace dockus.Dock.Controls;

public partial class SystemInfo : UserControl
{
    public TextBlock ClockTextBlock => ClockText;
    public TextBlock BatteryPercentTextBlock => BatteryPercentText;
    public ContentControl BatteryIconContentControl => BatteryIconControl;
    public Popup SystemTrayPopup => TrayPopup;

    public SystemInfo()
    {
        InitializeComponent();
        this.DataContext = dockus.Core.Models.AppSettings.Current;
    }

    private void Notifications_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.OpenNotifications();
    }

    private void Configuration_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.OpenSettings();
    }

    private void SystemTray_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.ToggleSystemTrayPopup();
    }
}