using dockus.Core.Interop;
using dockus.Core.Models;
using dockus.Dock;
using dockus.Dock.Controls;
using GongSolutions.Wpf.DragDrop;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace dockus;

public partial class MainWindow : Window, IDropTarget
{
    private IDockManager? _dockManager;

    public ObservableCollection<WindowItem> PinnedItems => _dockManager?.PinnedItems ?? new ObservableCollection<WindowItem>();
    public ObservableCollection<WindowItem> ActiveUnpinnedItems => _dockManager?.ActiveUnpinnedItems ?? new ObservableCollection<WindowItem>();

    public MainWindow()
    {
        InitializeComponent();
        this.DataContext = this;
    }

    #region Window Events
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _dockManager = new DockManager(
            window: this,
            systemInfo: SystemInfoControl,
            mainBorder: MainBorder
        );

        _dockManager.Initialize();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _dockManager?.Dispose();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _dockManager?.OnWindowSizeChanged();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;

        IntPtr extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(extendedStyle.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW));
    }
    #endregion

    #region Mouse Events
    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _dockManager?.OnMouseEnter();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _dockManager?.OnMouseLeave();
    }
    #endregion

    #region Public Methods for UserControls
    public void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            if (element.DataContext is WindowItem item)
            {
                _dockManager?.OnIconClick(item);
            }
        }
    }

    public void IconBlock_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            CustomMenu.PlacementTarget = element;
            CustomMenu.IsOpen = true;
        }
    }

    public void OpenNotifications()
    {
        Process.Start(new ProcessStartInfo("ms-actioncenter:") { UseShellExecute = true });
    }

    public void OpenSettings()
    {
        var settingsWindow = new dockus.Settings.SettingsWindow { Owner = this };
        settingsWindow.ShowDialog();
    }

    public void ToggleSystemTrayPopup()
    {
        SystemInfoControl.SystemTrayPopup.IsOpen = !SystemInfoControl.SystemTrayPopup.IsOpen;
    }
    #endregion

    #region Icon Interaction - Context Menu
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (e.Source is FrameworkElement { DataContext: WindowItem item })
        {
            _dockManager?.PinItem(item);
        }
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (e.Source is FrameworkElement { DataContext: WindowItem item })
        {
            _dockManager?.UnpinItem(item);
        }
    }
    #endregion

    #region Drag & Drop
    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        GongSolutions.Wpf.DragDrop.DragDrop.DefaultDropHandler.Drop(dropInfo);
        _dockManager?.SavePinnedApps();
    }

    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.TargetCollection == PinnedItems &&
            dropInfo.DragInfo.SourceCollection == PinnedItems)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = DragDropEffects.Move;
        }
    }
    #endregion

    #region Menu Actions
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void ToggleBar_Click(object sender, RoutedEventArgs e)
    {
        _dockManager?.ToggleTaskbar();
        if (sender is MenuItem menuItem)
        {
            menuItem.Header = (_dockManager?.IsTaskbarHidden == true) ? "Show Taskbar" : "Hide Taskbar";
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
    #endregion
}