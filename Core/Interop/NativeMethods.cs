using System.Runtime.InteropServices;
using System.Text;

namespace dockus.Core.Interop;

internal static class NativeMethods
{
    #region Battery
    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

    #endregion

    #region Hiding from alt+tab
    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPt64(IntPtr hWnd, int nIndex);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPt64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }


    #endregion


    #region Window Rectangles

    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IntersectRect(out RECT lprcDst, [In] ref RECT lprcSrc1, [In] ref RECT lprcSrc2);

    #endregion

    #region System Event Hooks

    [DllImport("user32.dll")]
    internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    internal delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    internal const uint WINEVENT_OUTOFCONTEXT = 0;
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;

    #endregion

    #region Window Placement (para comprobar si está maximizada)

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    internal const int SW_SHOWMAXIMIZED = 3;
    internal const int SW_SHOWMINIMIZED = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    #endregion


    #region user32.dll

    internal const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDesktopWindow();

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);
    internal delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpEnumFunc, ref GCHandle lParam);
    internal delegate bool EnumWindowsProc(IntPtr hWnd, ref GCHandle lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);


    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    internal static long GetWindowLong(IntPtr hWnd, int nIndex) => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLongPtr64(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", CharSet = CharSet.Unicode)]
    private static extern long GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", CharSet = CharSet.Unicode)]
    private static extern long GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    internal const int GW_OWNER = 4;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WM_GETICON = 0x007F;
    internal const int ICON_BIG = 1;
    internal const int ICON_SMALL2 = 2;
    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const int SW_RESTORE = 9;
    internal const int ABM_SETSTATE = 0x0000000A;
    internal const int ABS_AUTOHIDE = 0x0000001;
    internal const int ABS_ALWAYSONTOP = 0x0000002;

    #endregion

    #region kernel32.dll

    [DllImport("kernel32.dll")]
    internal static extern IntPtr OpenProcess(ProcessAccessFlags processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern int GetApplicationUserModelId(IntPtr hProcess, ref int applicationUserModelIdLength, StringBuilder applicationUserModelId);

    [Flags]
    internal enum ProcessAccessFlags : uint
    {
        QueryLimitedInformation = 0x1000,
    }

    #endregion

    #region shell32.dll

    [DllImport("shell32.dll", SetLastError = true)]
    internal static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int SHCreateItemInKnownFolder([MarshalAs(UnmanagedType.LPStruct)] Guid kfid, uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszPath, [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid, [Out, MarshalAs(UnmanagedType.Interface, IidParameterIndex = 4)] out IShellItem2 ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem { }

    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem2 : IShellItem
    {
        void BindToHandler(); void GetParent(); void GetDisplayName(); void GetAttributes(); void Compare(); void GetPropertyStore(); void GetPropertyStoreWithCreateObject(); void GetPropertyStoreForKeys(); void GetPropertyDescriptionList(); void Update();
        [PreserveSig] int GetProperty(ref PROPERTYKEY key, [Out, MarshalAs(UnmanagedType.Struct)] out object pv);
        void GetCLSID(); void GetFileTime(); void GetInt32(ref PROPERTYKEY key, out int pi);
        [PreserveSig] int GetString(ref PROPERTYKEY key, [Out, MarshalAs(UnmanagedType.LPWStr)] out string ppsz);
        void GetUInt32(); void GetUInt64(); void GetBool();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int SHCreateItemFromParsingName(
    [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
    IntPtr pbc,
    ref Guid riid,
    [MarshalAs(UnmanagedType.Interface)] out object ppv
)   ;


    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage([In] SIZE size, [In] SIIGBF flags, [Out] out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SIZE { public int cx; public int cy; }

    [Flags] internal enum SIIGBF { RESIZETOFIT = 0x00, BIGGERSIZEOK = 0x01, MEMORYONLY = 0x02, ICONONLY = 0x04, THUMBNAILONLY = 0x08, INCACHEONLY = 0x10, }

    internal static readonly Guid AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    internal static readonly PROPERTYKEY PKEY_ItemNameDisplay = new() { fmtid = new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), pid = 10 };

    #endregion

    #region dwmapi.dll

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    internal enum DWMWINDOWATTRIBUTE { DWMWA_CLOAKED = 14 }

    #endregion

    #region gdi32.dll

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);

    #endregion
}