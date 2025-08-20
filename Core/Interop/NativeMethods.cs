using System;
using System.Runtime.InteropServices;
using System.Text;

namespace dockus.Core.Interop;

internal static class NativeMethods
{
    // Window Management
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    // Window Enumeration & Info
    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpEnumFunc, ref GCHandle lParam);
    internal delegate bool EnumWindowsProc(IntPtr hWnd, ref GCHandle lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("User32.dll", SetLastError = true)]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("User32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    internal static long GetWindowLong(IntPtr hWnd, int nIndex) => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLongPtr64(hWnd, nIndex);

    [DllImport("User32.dll", EntryPoint = "GetWindowLong", CharSet = CharSet.Unicode)]
    private static extern long GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("User32.dll", EntryPoint = "GetWindowLongPtr", CharSet = CharSet.Unicode)]
    private static extern long GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    // DWM
    [DllImport("Dwmapi.dll", SetLastError = true)]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttributeToGet, ref int pvAttributeValue, int cbAttribute);
    internal enum DWMWINDOWATTRIBUTE { DWMWA_CLOAKED = 14 }

    // Shell
    [DllImport("Shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("shell32.dll", SetLastError = true)]
    internal static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    // Icons
    [DllImport("User32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    // Constants
    internal const int SW_RESTORE = 9;
    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const int GW_OWNER = 4;
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_CHILDWINDOW = 0x40000000;
    internal const int WS_EX_APPWINDOW = 0x00040000;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WM_GETICON = 0x007F;
    internal const int ICON_BIG = 1;
    internal const int ICON_SMALL2 = 2;
    internal const int ABM_SETSTATE = 0x0000000A;
    internal const int ABS_AUTOHIDE = 0x0000001;
    internal const int ABS_ALWAYSONTOP = 0x0000002;
}