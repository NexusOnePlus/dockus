using dockus.Core.Interop;
using dockus.Core.Models;
using dockus.Core.Services;
using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace dockus;

public partial class MainWindow : Window, IDropTarget
{
    private IntPtr m_hWnd = IntPtr.Zero;
    private readonly DispatcherTimer _updateTimer;
    private readonly DispatcherTimer _hideTimer;

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
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        m_hWnd = new WindowInteropHelper(this).Handle;
        this.MaxWidth = SystemParameters.WorkArea.Width;

        LoadPinnedApps();

        _updateTimer.Start();
        UpdateOpenWindows(null, EventArgs.Empty);
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _persistenceService.SavePinnedApps(PinnedItems);
        RestoreTaskbar();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
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
}