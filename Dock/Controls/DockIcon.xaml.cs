using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace dockus.Dock.Controls;

public partial class DockIcon : UserControl
{
    public DockIcon()
    {
        InitializeComponent();
    }

    private void Icon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.Icon_MouseLeftButtonDown(this, e);
    }

    private void IconBlock_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.IconBlock_MouseRightButtonDown(this, e);
    }

    private void IconBlock_MouseEnter(object sender, MouseEventArgs e)
    {
        VisualStateManager.GoToState(this, "MouseOver", true);
    }

    private void IconBlock_MouseLeave(object sender, MouseEventArgs e)
    {
        VisualStateManager.GoToState(this, "Normal", true);
    }

}