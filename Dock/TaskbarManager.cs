using dockus.Core.Interop;
using System;
using System.Windows.Threading;

namespace dockus.Dock;

public class TaskbarManager : IDisposable
{
    private readonly DispatcherTimer _hideTaskbarTimer;
    private bool _isHidden;

    public bool IsHidden => _isHidden;

    public TaskbarManager()
    {
        _hideTaskbarTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _hideTaskbarTimer.Tick += (s, e) => HideTaskbars();
    }

    public void Toggle()
    {
        if (_isHidden)
            ShowTaskbars();
        else
            StartAutoHide();
    }

    public void StartAutoHide()
    {
        _hideTaskbarTimer.Start();
        _isHidden = true;
    }

    public void StopAutoHide()
    {
        _hideTaskbarTimer.Stop();
        ShowTaskbars();
    }

    private void HideTaskbars()
    {
        HideTaskbar("Shell_TrayWnd");
        HideTaskbar("Secondary_TrayWnd");
    }

    private void ShowTaskbars()
    {
        _hideTaskbarTimer.Stop();
        ShowTaskbar("Shell_TrayWnd");
        ShowTaskbar("Secondary_TrayWnd");
        _isHidden = false;
    }

    private static void HideTaskbar(string className)
    {
        IntPtr hWnd = NativeMethods.FindWindow(className, null!);
        if (hWnd != IntPtr.Zero)
        {
            SetAppBarState(hWnd, NativeMethods.ABS_AUTOHIDE);
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_HIDE);
        }
    }

    private static void ShowTaskbar(string className)
    {
        IntPtr hWnd = NativeMethods.FindWindow(className, null!);
        if (hWnd != IntPtr.Zero)
        {
            SetAppBarState(hWnd, NativeMethods.ABS_ALWAYSONTOP);
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
        }
    }

    private static void SetAppBarState(IntPtr hWnd, int state)
    {
        var abd = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = hWnd,
            lParam = (IntPtr)state
        };
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref abd);
    }

    public void Dispose()
    {
        StopAutoHide();
    }
}
