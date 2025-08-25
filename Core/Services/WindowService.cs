using dockus.Core.Interop;
using dockus.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace dockus.Core.Services;

[SupportedOSPlatform("windows10.0.17763.0")]
public class WindowService
{
    public static int GetBatteryPercent()
    {
        if (NativeMethods.GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps))
        {
            return sps.BatteryLifePercent;
        }
        return -1;
    }

    public static bool IsCharging()
    {
        if (NativeMethods.GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps))
        {
            return sps.ACLineStatus == 1;
        }
        return false;
    }
    public bool ListWindows(IntPtr hWnd, ref GCHandle lParam, IntPtr mainWindowHandle)
    {
        if (lParam.Target is not List<WindowItem> list || !IsAppWindow(hWnd, mainWindowHandle))
        {
            return true;
        }

        string title = GetWindowTitle(hWnd);
        if (string.IsNullOrEmpty(title))
        {
            return true;
        }

        var item = new WindowItem { Hwnd = hWnd, Title = title, IsRunning = true };
        var (type, identifier) = GetIdentifierForWindow(hWnd);


        if (!string.IsNullOrEmpty(identifier))
        {
            item.Identifier = identifier;
            item.IdentifierType = type;
            item.Icon = (type == PinnedAppType.Aumid)
                ? GetIconFromAumid(item.Identifier, out _)
                : GetIconFromPath(item.Identifier);
        }

        item.Icon ??= GetSystemIcon(hWnd);
        list.Add(item);

        return true;
    }

    public ImageSource? GetIconForPinnedApp(PinnedApp app, out string? title)
    {
        title = app.Type == PinnedAppType.Path ? Path.GetFileNameWithoutExtension(app.Identifier) : null;
        return app.Type == PinnedAppType.Path
            ? GetIconFromPath(app.Identifier)
            : GetIconFromAumid(app.Identifier, out title);
    }

    #region Private: Window & Process Identification

    private (PinnedAppType Type, string? Identifier) GetIdentifierForWindow(IntPtr hWnd)
    {
        try
        {
            uint pid = GetRealProcessId(hWnd);
            if (pid == 0) return (PinnedAppType.Path, null);

            IntPtr processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessAccessFlags.QueryLimitedInformation, false, (int)pid);
            if (processHandle == IntPtr.Zero)
            {
                return (PinnedAppType.Path, GetPathForDesktopApp(pid));
            }

            try
            {
                int bufferLength = 1024;
                var buffer = new StringBuilder(bufferLength);
                if (NativeMethods.GetApplicationUserModelId(processHandle, ref bufferLength, buffer) == 0)
                {
                    string aumid = buffer.ToString();
                    return (PinnedAppType.Aumid, aumid);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(processHandle);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WARN] Failed to get AUMID, falling back to path. Error: {ex.Message}");
        }

        uint finalPid = GetRealProcessId(hWnd);
        return (PinnedAppType.Path, GetPathForDesktopApp(finalPid));
    }

    private uint GetRealProcessId(IntPtr hWnd)
    {
        NativeMethods.GetWindowThreadProcessId(hWnd, out uint initialPid);
        if (initialPid == 0) return 0;

        try
        {
            using var initialProc = Process.GetProcessById((int)initialPid);
            if (initialProc.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var childHwnd in GetChildWindows(hWnd))
                {
                    NativeMethods.GetWindowThreadProcessId(childHwnd, out uint childPid);
                    if (childPid != 0 && childPid != initialPid)
                    {
                        return childPid;
                    }
                }
            }
        }
        catch { /* Ignore */ }

        return initialPid;
    }

    private List<IntPtr> GetChildWindows(IntPtr parent)
    {
        var result = new List<IntPtr>();
        var listHandle = GCHandle.Alloc(result);
        try
        {
            NativeMethods.EnumChildWindows(parent, (hWnd, lParam) =>
            {
                if (GCHandle.FromIntPtr(lParam).Target is List<IntPtr> list) list.Add(hWnd);
                return true;
            }, GCHandle.ToIntPtr(listHandle));
        }
        finally
        {
            if (listHandle.IsAllocated) listHandle.Free();
        }
        return result;
    }

    private string? GetPathForDesktopApp(uint pid)
    {
        if (pid == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch { return null; }
    }

    #endregion

    #region Private: Icon & Title Retrieval

    private ImageSource? GetIconFromAumid(string aumid, out string? title)
    {
        title = null;
        NativeMethods.IShellItem2? shellItem = null;
        IntPtr hBitmap = IntPtr.Zero;
        const int ICON_SIZE = 48;

        try
        {
            if (NativeMethods.SHCreateItemInKnownFolder(NativeMethods.AppsFolder, 0, aumid, typeof(NativeMethods.IShellItem2).GUID, out shellItem) != 0 || shellItem == null)
            {
                return null;
            }

            var pkey = NativeMethods.PKEY_ItemNameDisplay;
            if (shellItem.GetString(ref pkey, out string displayName) == 0)
            {
                title = displayName;
            }

            var imageFactory = (NativeMethods.IShellItemImageFactory)shellItem;
            var size = new NativeMethods.SIZE { cx = ICON_SIZE, cy = ICON_SIZE };

            if (imageFactory.GetImage(size, NativeMethods.SIIGBF.ICONONLY, out hBitmap) == 0 && hBitmap != IntPtr.Zero)
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze();
                return bitmapSource;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Exception in GetIconFromAumid for '{aumid}': {ex.Message}");
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
            if (shellItem != null) Marshal.ReleaseComObject(shellItem);
        }

        return null;
    }

    private BitmapSource? GetIconFromPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            if (NativeMethods.ExtractIconEx(path, 0, out IntPtr hLarge, out _, 1) > 0 && hLarge != IntPtr.Zero)
            {
                try { return Imaging.CreateBitmapSourceFromHIcon(hLarge, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
                finally { NativeMethods.DestroyIcon(hLarge); }
            }
        }
        catch { /* Ignore */ }
        return null;
    }

    private BitmapSource? GetSystemIcon(IntPtr hWnd)
    {
        IntPtr hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero);
        if (hIcon == IntPtr.Zero) hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero);
        if (hIcon != IntPtr.Zero)
        {
            return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        return null;
    }

    #endregion

    #region Private: Window Helpers

    private bool IsAppWindow(IntPtr hWnd, IntPtr mainWindowHandle)
    {
        if (hWnd == mainWindowHandle || !NativeMethods.IsWindowVisible(hWnd) || NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return false;
        long exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;
        int cloaked = 0;
        NativeMethods.DwmGetWindowAttribute(hWnd, (int)NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, ref cloaked, Marshal.SizeOf<int>());
        return cloaked == 0;
    }

    private string GetWindowTitle(IntPtr hWnd)
    {
        int length = NativeMethods.GetWindowTextLength(hWnd);
        if (length == 0) return string.Empty;
        var sb = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    #endregion
}