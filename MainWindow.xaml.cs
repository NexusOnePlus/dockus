using dockus.Core.Interop;
using dockus.Core.Models;
using dockus.Core.Services;
using GongSolutions.Wpf.DragDrop;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace dockus;

public partial class MainWindow : Window, IDropTarget
{
    private IntPtr m_hWnd = IntPtr.Zero;
    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _dockHideDelayTimer;
    private bool _isInteractionPending = false;
    private const double Y_OFFSET = 60.0; //just in case



    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private IntPtr _winEventHook = IntPtr.Zero;


    // Services
    private readonly PersistenceService _persistenceService = new();
    private readonly WindowService _windowService = new();
    private readonly AppLauncherService _appLauncherService = new();

    public ObservableCollection<WindowItem> PinnedItems { get; set; }
    public ObservableCollection<WindowItem> ActiveUnpinnedItems { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        PinnedItems = new ObservableCollection<WindowItem>();
        ActiveUnpinnedItems = new ObservableCollection<WindowItem>();
        this.DataContext = this;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _hideTimer.Tick += HideTimer_Tick;

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _updateTimer.Tick += UpdateOpenWindows;

        _dockHideDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _dockHideDelayTimer.Tick += DockHideDelay_Tick;

    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        m_hWnd = new WindowInteropHelper(this).Handle;
        this.MaxWidth = SystemParameters.WorkArea.Width;
        // this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth ) / 2;
        // this.Top = SystemParameters.PrimaryScreenHeight - this.ActualHeight;

        PositionWindow();
        this.Top = _hiddenTop;

        LoadPinnedApps();

        _updateTimer.Start();
        // UpdateOpenWindows(null, EventArgs.Empty);

        _winEventDelegate = new NativeMethods.WinEventDelegate(WinEventProc);
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, _winEventDelegate, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        UpdateDockVisibility(true);
    }

    private void PositionWindow()
    {
        if (m_hWnd == IntPtr.Zero || MainBorder == null || this.ActualHeight == 0) return;

        double screenHeight = SystemParameters.PrimaryScreenHeight;
        double visibleDockHeight = MainBorder.ActualHeight;

        _shownTop = screenHeight - visibleDockHeight - 4;
        _hiddenTop = screenHeight - TRIGGER_HEIGHT;

        this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
    }


    private void Window_Closing(object sender, CancelEventArgs e)
    {
        NativeMethods.UnhookWinEvent(_winEventHook);
        _persistenceService.SavePinnedApps(PinnedItems);
        RestoreTaskbar();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
        PositionWindow();
    }

    private void UpdateOpenWindows(object? sender, EventArgs e)
    {
        var liveWindows = new List<WindowItem>();
        var handle = GCHandle.Alloc(liveWindows);
        try
        {
            NativeMethods.EnumDesktopWindows(
            IntPtr.Zero,
            delegate (IntPtr hWnd, ref GCHandle lParam) { return _windowService.ListWindows(hWnd, ref lParam, m_hWnd); },
            ref handle);
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }

        foreach (var pinnedItem in PinnedItems)
        {
            var runningInstance = liveWindows.FirstOrDefault(w => w.Identifier == pinnedItem.Identifier);
            pinnedItem.IsRunning = runningInstance != null;
            if (runningInstance != null)
            {
                pinnedItem.Hwnd = runningInstance.Hwnd;
                pinnedItem.Title = runningInstance.Title;
            }
        }

        var closedItems = ActiveUnpinnedItems.Where(item => !liveWindows.Any(w => w.Hwnd == item.Hwnd)).ToList();
        foreach (var item in closedItems)
        {
            ActiveUnpinnedItems.Remove(item);
        }

        foreach (var existingItem in ActiveUnpinnedItems)
        {
            var liveData = liveWindows.FirstOrDefault(w => w.Hwnd == existingItem.Hwnd);
            if (liveData != null)
            {
                if (existingItem.Identifier != liveData.Identifier)
                {
                    existingItem.Identifier = liveData.Identifier;
                    existingItem.IdentifierType = liveData.IdentifierType;
                    existingItem.Icon = liveData.Icon;
                }
                existingItem.Title = liveData.Title;
            }
        }

        var newItems = liveWindows
            .Where(w => !PinnedItems.Any(p => p.Identifier == w.Identifier) &&
                        !ActiveUnpinnedItems.Any(item => item.Hwnd == w.Hwnd))
            .ToList();

        foreach (var item in newItems)
        {
            ActiveUnpinnedItems.Add(item);
        }
    }

    #region Taskbar and Settings

    private bool _isBarVisible = true;

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new dockus.Settings.SettingsWindow { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void ToggleBar_Click(object sender, RoutedEventArgs e)
    {
        if (_isBarVisible)
        {
            _hideTimer.Start();
            _isBarVisible = false;
            if (sender is MenuItem menuItem) menuItem.Header = "Show Taskbar";
        }
        else
        {
            _hideTimer.Stop();
            RestoreTaskbar();
            _isBarVisible = true;
            if (sender is MenuItem menuItem) menuItem.Header = "Hide Taskbar";
        }
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        IntPtr taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null!);
        if (taskbarHwnd != IntPtr.Zero)
        {
            SetAppBarState(taskbarHwnd, NativeMethods.ABS_AUTOHIDE);
            NativeMethods.ShowWindow(taskbarHwnd, NativeMethods.SW_HIDE);
        }

        IntPtr secondaryTaskbarHwnd = NativeMethods.FindWindow("Secondary_TrayWnd", null!);
        if (secondaryTaskbarHwnd != IntPtr.Zero)
        {
            SetAppBarState(secondaryTaskbarHwnd, NativeMethods.ABS_AUTOHIDE);
            NativeMethods.ShowWindow(secondaryTaskbarHwnd, NativeMethods.SW_HIDE);
        }
    }

    private void RestoreTaskbar()
    {
        IntPtr taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null!);
        if (taskbarHwnd != IntPtr.Zero)
        {
            SetAppBarState(taskbarHwnd, NativeMethods.ABS_ALWAYSONTOP);
            NativeMethods.ShowWindow(taskbarHwnd, NativeMethods.SW_SHOW);
        }

        IntPtr secondaryTaskbarHwnd = NativeMethods.FindWindow("Secondary_TrayWnd", null!);
        if (secondaryTaskbarHwnd != IntPtr.Zero)
        {
            SetAppBarState(secondaryTaskbarHwnd, NativeMethods.ABS_ALWAYSONTOP);
            NativeMethods.ShowWindow(secondaryTaskbarHwnd, NativeMethods.SW_SHOW);
        }
    }

    private static void SetAppBarState(IntPtr taskbarHwnd, int state)
    {
        var abd = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = taskbarHwnd,
            lParam = (IntPtr)state
        };
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref abd);
    }

    #endregion

    #region User Interaction
    private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Debug.WriteLine("[LOG] Icon clicked. Grace period activated.");
        _isInteractionPending = true;
        _dockHideDelayTimer.Stop();
        if (sender is FrameworkElement { DataContext: WindowItem item })
        {
            if (item.IsRunning && item.Hwnd != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(item.Hwnd, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(item.Hwnd);
            }
            else if (item.IsPinned)
            {
                _appLauncherService.LaunchApp(item);
            }
        }
    }

    void IDropTarget.Drop(IDropInfo dropInfo)
    {
        GongSolutions.Wpf.DragDrop.DragDrop.DefaultDropHandler.Drop(dropInfo);
        _persistenceService.SavePinnedApps(PinnedItems);
    }

    void IDropTarget.DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.TargetCollection == PinnedItems && dropInfo.DragInfo.SourceCollection == PinnedItems)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = DragDropEffects.Move;
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (e.Source is FrameworkElement { DataContext: WindowItem item })
        {
            if (string.IsNullOrEmpty(item.Identifier)) return;

            item.IsPinned = true;
            if (!PinnedItems.Any(p => p.Identifier == item.Identifier))
            {
                PinnedItems.Add(item);
            }
            ActiveUnpinnedItems.Remove(item);
            _persistenceService.SavePinnedApps(PinnedItems);
        }
    }

    private void Unpin_Click(object sender, RoutedEventArgs e)
    {
        if (e.Source is FrameworkElement { DataContext: WindowItem item })
        {
            item.IsPinned = false;
            PinnedItems.Remove(item);

            if (item.IsRunning)
            {
                ActiveUnpinnedItems.Add(item);
            }
            _persistenceService.SavePinnedApps(PinnedItems);
        }
    }

    #endregion

    #region Persistence
    private void LoadPinnedApps()
    {
        var pinnedList = _persistenceService.LoadPinnedApps();
        PinnedItems.Clear();
        foreach (var pinnedApp in pinnedList)
        {
            var item = new WindowItem
            {
                IsPinned = true,
                Identifier = pinnedApp.Identifier,
                IdentifierType = pinnedApp.Type,
                IsRunning = false,
                Icon = _windowService.GetIconForPinnedApp(pinnedApp, out string? title),
                Title = title ?? "App"
            };
            PinnedItems.Add(item);
        }
    }
    #endregion
    private const double TRIGGER_HEIGHT = 2.0;
    private double _hiddenTop;
    private double _shownTop;
    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        Debug.WriteLine("[LOG] Mouse ENTER. Cancelling hide timer.");
        _dockHideDelayTimer.Stop();
        AnimateDock(_shownTop);
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isInteractionPending)
        {
            Debug.WriteLine("[LOG] Mouse LEAVE. Grace period active, not hiding.");
            _isInteractionPending = false;
            return;
        }
        Debug.WriteLine("[LOG] Mouse LEAVE. Starting 4-second hide timer.");
        _dockHideDelayTimer.Start();
    }

    private void DockHideDelay_Tick(object? sender, EventArgs e)
    {
        _dockHideDelayTimer.Stop();
        if (!this.IsMouseOver)
        {
            UpdateDockVisibility(false);
        }
    }

    private void PeekTimer_Tick(object? sender, EventArgs e)
    {
        if (this.IsMouseOver || _dockHideDelayTimer.IsEnabled || _isInteractionPending)
        {
            return;
        }

        Debug.WriteLine("[LOG] Peek Timer: Checking for window behind dock...");
        if (IsWindowBehindDock())
        {
            if (this.Top != _hiddenTop) AnimateDock(_hiddenTop);
        }
        else
        {
            if (this.Top != _shownTop) AnimateDock(_shownTop);
        }
    }

    private bool IsWindowBehindDock()
    {
        if (m_hWnd == IntPtr.Zero) return true;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget == null) return true;

        Matrix matrix = source.CompositionTarget.TransformToDevice;
        var logicalPoint = new Point(this.Left + this.ActualWidth / 2, _shownTop + (MainBorder?.ActualHeight ?? 0) / 2);
        var physicalPoint = matrix.Transform(logicalPoint);
        var screenPoint = new NativeMethods.POINT { X = (int)physicalPoint.X, Y = (int)physicalPoint.Y };

        IntPtr hWndAtPoint = NativeMethods.WindowFromPoint(screenPoint);

        if (hWndAtPoint == IntPtr.Zero || hWndAtPoint == m_hWnd || hWndAtPoint == NativeMethods.GetDesktopWindow())
        {
            return false;
        }

        long style = NativeMethods.GetWindowLong(hWndAtPoint, NativeMethods.GWL_EXSTYLE);
        bool isVisible = NativeMethods.IsWindowVisible(hWndAtPoint);

        bool isAppWindow = (style & NativeMethods.WS_EX_APPWINDOW) == NativeMethods.WS_EX_APPWINDOW;
        bool isRealAppWindow = isVisible && isAppWindow;

        var classNameBuilder = new StringBuilder(256);
        NativeMethods.GetClassName(hWndAtPoint, classNameBuilder, classNameBuilder.Capacity);

        Debug.WriteLine($"[LOG] IsWindowBehindDock Check:");
        Debug.WriteLine($"  -> HWND Found: {hWndAtPoint}");
        Debug.WriteLine($"  -> Class Name: '{classNameBuilder}'");
        Debug.WriteLine($"  -> Is Visible? {isVisible}");
        Debug.WriteLine($"  -> Is App Window (Taskbar)? {isAppWindow}");
        Debug.WriteLine($"  -> FINAL DECISION: A REAL app window is behind the dock = {isRealAppWindow}");

        return isRealAppWindow;
    }

    private void AnimateDock(double toValue)
    {
        if (Math.Abs(this.Top - toValue) < 1) return;

        var animation = new DoubleAnimation
        {
            To = toValue,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        this.BeginAnimation(Window.TopProperty, animation);
    }
    void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        Dispatcher.BeginInvoke(new Action(() => UpdateDockVisibility(false)));
    }
    private void UpdateDockVisibility(bool isInitialLoad)
    {
        if (this.IsMouseOver || _dockHideDelayTimer.IsEnabled || _isInteractionPending)
        {
            return;
        }

        IntPtr foregroundHWnd = NativeMethods.GetForegroundWindow();
        bool shouldHide = false;

        IntPtr progman = NativeMethods.FindWindow("Progman", String.Empty);
        IntPtr workerw = NativeMethods.FindWindow("WorkerW", String.Empty);

        if (foregroundHWnd == progman || foregroundHWnd == workerw)
        {
            shouldHide = false;
            Debug.WriteLine($"[LOGIC] UpdateDockVisibility: Foreground is the Desktop Shell ({foregroundHWnd}). Showing dock.");
        }
        else if (foregroundHWnd != IntPtr.Zero && foregroundHWnd != m_hWnd)
        {
            NativeMethods.GetWindowRect(foregroundHWnd, out NativeMethods.RECT windowRect);

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                Matrix matrix = source.CompositionTarget.TransformToDevice;

                var dockLogicalRect = new Rect(this.Left, _shownTop, this.ActualWidth, MainBorder.ActualHeight);
                Point physicalTopLeft = matrix.Transform(dockLogicalRect.TopLeft);
                Point physicalBottomRight = matrix.Transform(dockLogicalRect.BottomRight);

                var dockRect = new NativeMethods.RECT
                {
                    left = (int)physicalTopLeft.X,
                    top = (int)physicalTopLeft.Y,
                    right = (int)physicalBottomRight.X,
                    bottom = (int)physicalBottomRight.Y
                };

                if (NativeMethods.IntersectRect(out _, ref windowRect, ref dockRect))
                {
                    shouldHide = true;
                }
            }
        }

        Debug.WriteLine($"[LOGIC] UpdateDockVisibility: Foreground HWND is {foregroundHWnd}. Intersects = {shouldHide}");

        double targetTop = shouldHide ? _hiddenTop : _shownTop;

        if (isInitialLoad)
        {
            this.Top = targetTop;
        }
        else
        {
            AnimateDock(targetTop);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        RestoreTaskbar();
        Application.Current.Shutdown();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;

        IntPtr extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
            new IntPtr(extendedStyle.ToInt64() | NativeMethods.WS_EX_TOOLWINDOW));
    }

}