using dockus.Core.Interop;
using dockus.Core.Models;
using dockus.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;

namespace dockus.Dock;

public class DockManager : IDockManager
{
    #region Fields
    private readonly Window _window;
    private IntPtr _hWnd = IntPtr.Zero;
    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _dockHideDelayTimer;
    private readonly DispatcherTimer _clockTimer;
    private bool _isInteractionPending = false;
    private const double TRIGGER_HEIGHT = 2.0;
    private double _hiddenTop;
    private double _shownTop;

    private NativeMethods.WinEventDelegate? _winEventDelegate;
    private IntPtr _winEventHook = IntPtr.Zero;

    private readonly PersistenceService _persistenceService;
    private readonly WindowService _windowService;
    private readonly AppLauncherService _appLauncherService;
    private readonly TaskbarManager _taskbarManager;

    private readonly TextBlock _clockText;
    private readonly TextBlock _batteryPercentText;
    private readonly ContentControl _batteryIconControl;
    private readonly Border _mainBorder;
    #endregion

    #region Properties
    public ObservableCollection<WindowItem> PinnedItems { get; }
    public ObservableCollection<WindowItem> ActiveUnpinnedItems { get; }
    #endregion

    #region Constructor
    public DockManager(Window window, Controls.SystemInfo systemInfo, Border mainBorder)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _mainBorder = mainBorder ?? throw new ArgumentNullException(nameof(mainBorder));

        if (systemInfo == null) throw new ArgumentNullException(nameof(systemInfo));
        _clockText = systemInfo.ClockTextBlock;
        _batteryPercentText = systemInfo.BatteryPercentTextBlock;
        _batteryIconControl = systemInfo.BatteryIconContentControl;

        PinnedItems = new ObservableCollection<WindowItem>();
        ActiveUnpinnedItems = new ObservableCollection<WindowItem>();

        _persistenceService = new PersistenceService();
        _windowService = new WindowService();
        _appLauncherService = new AppLauncherService();
        _taskbarManager = new TaskbarManager();

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _updateTimer.Tick += UpdateOpenWindows;

        _dockHideDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _dockHideDelayTimer.Tick += DockHideDelay_Tick;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (s, e) =>
        {
            UpdateTime();
            UpdateBattery();
        };
    }
    #endregion

    #region Initialization
    public void Initialize()
    {
        _hWnd = new WindowInteropHelper(_window).Handle;
        _window.MaxWidth = SystemParameters.WorkArea.Width;

        PositionWindow();
        _window.Top = _hiddenTop;

        LoadPinnedApps();

        _updateTimer.Start();
        _clockTimer.Start();

        SetupWinEventHook();

        UpdateTime();
        UpdateBattery();
        UpdateDockVisibility(true);
    }

    private void SetupWinEventHook()
    {
        _winEventDelegate = new NativeMethods.WinEventDelegate(WinEventProc);
        _winEventHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero,
            _winEventDelegate,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);
    }
    #endregion

    #region Window Positioning
    private void PositionWindow()
    {
        if (_hWnd == IntPtr.Zero || _mainBorder == null || _window.ActualHeight == 0)
            return;

        double screenHeight = SystemParameters.PrimaryScreenHeight;
        double visibleDockHeight = _mainBorder.ActualHeight;

        _shownTop = screenHeight - visibleDockHeight - 4;
        _hiddenTop = screenHeight - TRIGGER_HEIGHT;

        _window.Left = (SystemParameters.WorkArea.Width - _window.ActualWidth) / 2;
    }

    public void OnWindowSizeChanged()
    {
        PositionWindow();
    }
    #endregion

    #region Clock and Battery Updates
    private void UpdateTime()
    {
        _clockText.Text = DateTime.Now.ToString("hh:mm tt").ToLower();
    }

    private void UpdateBattery()
    {
        int percent = WindowService.GetBatteryPercent();
        bool charging = WindowService.IsCharging();

        _batteryPercentText.Text = $"{percent}%";

        if (_batteryIconControl.Content is Viewbox vb && vb.Child is Canvas rootCanvas)
        {
            var inner = rootCanvas.Children.OfType<Canvas>().FirstOrDefault();
            var body = inner?.Children.OfType<System.Windows.Shapes.Rectangle>()
                .FirstOrDefault(r => r.Name == "Battery_body");

            if (body != null)
            {
                if (charging)
                {
                    body.Fill = System.Windows.Media.Brushes.Blue;
                }
                else if (percent > 50)
                {
                    body.Fill = System.Windows.Media.Brushes.Green;
                }
                else if (percent > 20)
                {
                    body.Fill = System.Windows.Media.Brushes.Orange;
                }
                else
                {
                    body.Fill = System.Windows.Media.Brushes.Red;
                }
            }
        }
    }
    #endregion

    #region Window Management
    private void UpdateOpenWindows(object? sender, EventArgs e)
    {
        var liveWindows = new List<WindowItem>();
        var handle = GCHandle.Alloc(liveWindows);
        try
        {
            NativeMethods.EnumDesktopWindows(
                IntPtr.Zero,
                delegate (IntPtr hWnd, ref GCHandle lParam)
                {
                    return _windowService.ListWindows(hWnd, ref lParam, _hWnd);
                },
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

        var closedItems = ActiveUnpinnedItems.Where(item =>
            !liveWindows.Any(w => w.Hwnd == item.Hwnd)).ToList();
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
    #endregion

    #region Mouse Interaction
    public void OnMouseEnter()
    {
        Debug.WriteLine("[LOG] Mouse ENTER. Cancelling hide timer.");
        _dockHideDelayTimer.Stop();
        AnimateDock(_shownTop);
    }

    public void OnMouseLeave()
    {
        if (_isInteractionPending)
        {
            Debug.WriteLine("[LOG] Mouse LEAVE. Grace period active, not hiding.");
            _isInteractionPending = false;
            return;
        }
        Debug.WriteLine("[LOG] Mouse LEAVE. Starting hide timer.");
        _dockHideDelayTimer.Start();
    }

    private void DockHideDelay_Tick(object? sender, EventArgs e)
    {
        _dockHideDelayTimer.Stop();
        if (!_window.IsMouseOver)
        {
            UpdateDockVisibility(false);
        }
    }
    #endregion

    #region Icon Interaction
    public void OnIconClick(WindowItem item)
    {
        Debug.WriteLine("[LOG] Icon clicked. Grace period activated.");
        _isInteractionPending = true;
        _dockHideDelayTimer.Stop();

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

    public void PinItem(WindowItem item)
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

    public void UnpinItem(WindowItem item)
    {
        item.IsPinned = false;
        PinnedItems.Remove(item);

        if (item.IsRunning)
        {
            ActiveUnpinnedItems.Add(item);
        }
        _persistenceService.SavePinnedApps(PinnedItems);
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

    public void SavePinnedApps()
    {
        _persistenceService.SavePinnedApps(PinnedItems);
    }
    #endregion

    #region Dock Visibility Logic
    private void AnimateDock(double toValue)
    {
        if (Math.Abs(_window.Top - toValue) < 1) return;

        var animation = new DoubleAnimation
        {
            To = toValue,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _window.BeginAnimation(Window.TopProperty, animation);
    }

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
                             int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        _window.Dispatcher.BeginInvoke(new Action(() => UpdateDockVisibility(false)));
    }

    private void UpdateDockVisibility(bool isInitialLoad)
    {
        if (_window.IsMouseOver || _dockHideDelayTimer.IsEnabled || _isInteractionPending)
        {
            return;
        }

        IntPtr foregroundHWnd = NativeMethods.GetForegroundWindow();
        bool shouldHide = false;

        if (IsWindowFromDesktopOrShell(foregroundHWnd))
        {
            shouldHide = false;
        }
        else if (foregroundHWnd != IntPtr.Zero && foregroundHWnd != _hWnd)
        {
            NativeMethods.GetWindowRect(foregroundHWnd, out NativeMethods.RECT windowRect);

            var source = PresentationSource.FromVisual(_window);
            if (source?.CompositionTarget != null)
            {
                Matrix matrix = source.CompositionTarget.TransformToDevice;

                var dockLogicalRect = new Rect(_window.Left, _shownTop,
                                              _window.ActualWidth, _mainBorder.ActualHeight);
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

        Debug.WriteLine($"[LOGIC] UpdateDockVisibility: Foreground HWND is {foregroundHWnd}. Should hide = {shouldHide}");

        double targetTop = shouldHide ? _hiddenTop : _shownTop;

        if (isInitialLoad)
        {
            _window.Top = targetTop;
        }
        else
        {
            AnimateDock(targetTop);
        }
    }

    private bool IsWindowFromDesktopOrShell(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;

        IntPtr progman = NativeMethods.FindWindow("Progman", string.Empty);
        IntPtr workerw = NativeMethods.FindWindow("WorkerW", string.Empty);
        IntPtr shellWindow = NativeMethods.GetShellWindow();
        IntPtr desktopWindow = NativeMethods.GetDesktopWindow();

        if (hwnd == progman || hwnd == workerw || hwnd == shellWindow || hwnd == desktopWindow)
            return true;

        var classNameSb = new StringBuilder(256);
        IntPtr cur = hwnd;
        while (cur != IntPtr.Zero)
        {
            NativeMethods.GetClassName(cur, classNameSb, classNameSb.Capacity);
            var cls = classNameSb.ToString();

            if (string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "SysListView32", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (cur == shellWindow || cur == desktopWindow)
                return true;

            cur = NativeMethods.GetParent(cur);
            classNameSb.Clear();
        }

        return false;
    }
    #endregion

    #region Taskbar Management
    public void ToggleTaskbar()
    {
        _taskbarManager.Toggle();
    }

    public bool IsTaskbarHidden => _taskbarManager.IsHidden;
    #endregion

    #region IDisposable
    public void Dispose()
    {
        _updateTimer?.Stop();
        _dockHideDelayTimer?.Stop();
        _clockTimer?.Stop();

        if (_winEventHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }

        _taskbarManager?.Dispose();
        _persistenceService?.SavePinnedApps(PinnedItems);
    }
    #endregion
}