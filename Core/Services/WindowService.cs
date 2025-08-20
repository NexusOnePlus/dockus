using dockus.Core.Interop;
using dockus.Core.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace dockus.Core.Services;

public class WindowService
{
    private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public ImageSource? GetIconForPinnedApp(PinnedApp app, out string? title)
    {
        title = null;
        if (app.Type == PinnedAppType.Path)
        {
            title = Path.GetFileNameWithoutExtension(app.Identifier);
            return GetIconFromPath(app.Identifier);
        }
        else // AUMID
        {
            return GetIconFromAumid(app.Identifier, out title);
        }
    }

    public bool ListWindows(IntPtr hWnd, ref GCHandle lParam, IntPtr mainWindowHandle)
    {
        if (lParam.Target is not System.Collections.Generic.List<WindowItem> list) return true;

        long nStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
        long nExStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

        if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero || (nExStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

        int nCloaked = 0;
        NativeMethods.DwmGetWindowAttribute(hWnd, (int)NativeMethods.DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, ref nCloaked, Marshal.SizeOf<int>());
        if (nCloaked != 0) return true;

        if ((nExStyle & NativeMethods.WS_EX_APPWINDOW) == 0 && NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return true;
        if ((nStyle & NativeMethods.WS_CHILDWINDOW) != 0) return true;

        int nTextLength = NativeMethods.GetWindowTextLength(hWnd);
        if (nTextLength++ > 0)
        {
            var sbText = new StringBuilder(nTextLength);
            NativeMethods.GetWindowText(hWnd, sbText, nTextLength);
            string sTitle = sbText.ToString();

            if (hWnd != mainWindowHandle && !string.IsNullOrEmpty(sTitle))
            {
                var item = new WindowItem { Hwnd = hWnd, Title = sTitle, IsRunning = true };

                string? aumid = GetAumidForWindow(hWnd);
                if (!string.IsNullOrEmpty(aumid))
                {
                    item.Identifier = aumid;
                    item.IdentifierType = PinnedAppType.Aumid;
                    item.Icon = GetIconFromAumid(item.Identifier, out _);
                }
                else
                {
                    string? path = GetPathForWindow(hWnd);
                    if (!string.IsNullOrEmpty(path))
                    {
                        item.Identifier = path;
                        item.IdentifierType = PinnedAppType.Path;
                        item.Icon = GetIconFromPath(item.Identifier);
                    }
                }

                item.Icon ??= GetWindowIcon(hWnd);
                list.Add(item);
            }
        }
        return true;
    }

    private BitmapSource? GetIconFromPath(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            if (NativeMethods.ExtractIconEx(path, 0, out IntPtr hLarge, out _, 1) > 0 && hLarge != IntPtr.Zero)
            {
                try
                {
                    return Imaging.CreateBitmapSourceFromHIcon(hLarge, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
                finally
                {
                    NativeMethods.DestroyIcon(hLarge);
                }
            }
        }
        catch { /* Fall through to return null */ }
        return null;
    }

    private ImageSource? GetIconFromAumid(string aumid, out string? title)
    {
        title = null;
        if (string.IsNullOrEmpty(aumid)) return null;

        try
        {
            string[] sParts = aumid.Split('!');
            if (sParts.Length < 1) return null;

            var pm = new PackageManager();
            var package = pm.FindPackagesForUser(string.Empty, sParts[0]).FirstOrDefault();
            if (package?.InstalledLocation?.Path == null) return null;

            string manifestPath = Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return null;

            XDocument manifest = XDocument.Load(manifestPath);
            XNamespace nsManifest = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
            XNamespace nsUap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

            var appElem = manifest.Root?.Descendants(nsManifest + "Application").FirstOrDefault(e => sParts.Length > 1 && e.Attribute("Id")?.Value == sParts[1])
                       ?? manifest.Root?.Descendants(nsManifest + "Application").FirstOrDefault();

            if (appElem == null) return null;

            var visual = appElem.Descendants(nsUap + "VisualElements").FirstOrDefault();
            title = visual?.Attribute("DisplayName")?.Value;

            var logoAttributes = new[] { "Square150x150Logo", "Square71x71Logo", "Square44x44Logo", "Logo" };
            string? sIconPath = logoAttributes
                .Select(attr => visual?.Attribute(attr)?.Value)
                .FirstOrDefault(path => !string.IsNullOrEmpty(path));

            if (!string.IsNullOrEmpty(sIconPath))
            {
                string sIconFile = Path.Combine(package.InstalledLocation.Path, sIconPath);
                if (File.Exists(sIconFile))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(sIconFile, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }

            string? executable = appElem.Attribute("Executable")?.Value;
            if (!string.IsNullOrEmpty(executable))
            {
                string exePath = Path.Combine(package.InstalledLocation.Path, executable);
                return GetIconFromPath(exePath);
            }
        }
        catch { /* Fall through to return null */ }
        return null;
    }

    private string? GetAumidForWindow(IntPtr hWnd)
    {
        try
        {
            Guid iid = IID_IPropertyStore;
            if (NativeMethods.SHGetPropertyStoreForWindow(hWnd, ref iid, out IPropertyStore pPropertyStore) == 0)
            {
                try
                {
                    PROPERTYKEY key = PKEY_AppUserModel_ID;
                    if (pPropertyStore.GetValue(ref key, out PROPVARIANT propVar) == 0)
                    {
                        return Marshal.PtrToStringUni(propVar.pwszVal);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(pPropertyStore);
                }
            }
        }
        catch { /* Fall through to return null */ }
        return null;
    }

    private string? GetPathForWindow(IntPtr hWnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return null;
            using var proc = Process.GetProcessById((int)pid);
            return proc?.MainModule?.FileName;
        }
        catch { return null; }
    }

    private BitmapSource? GetWindowIcon(IntPtr hWnd)
    {
        IntPtr hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero);
        if (hIcon == IntPtr.Zero) hIcon = NativeMethods.SendMessage(hWnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero);
        if (hIcon != IntPtr.Zero)
        {
            return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        return null;
    }
}