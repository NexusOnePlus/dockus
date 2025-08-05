using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Windows.Management.Deployment;
using static dockus.MainWindow;

namespace dockus
{
    public partial class MainWindow : Window
    {
        private IntPtr m_hWnd = IntPtr.Zero;
        private DispatcherTimer _updateTimer;

        #region P/Invoke Definitions


        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        public enum HRESULT : int
        {
            S_OK = 0,
            E_NOINTERFACE = unchecked((int)0x80004002),
            E_FAIL = unchecked((int)0x80004005),
        }

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("User32.dll", SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("User32.dll", SetLastError = true)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpEnumFunc, ref IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, ref IntPtr lParam);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        public const int GW_OWNER = 4;

        public static long GetWindowLong(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLongPtr64(hWnd, nIndex);
        }

        [DllImport("User32.dll", EntryPoint = "GetWindowLong", CharSet = CharSet.Unicode)]
        public static extern long GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("User32.dll", EntryPoint = "GetWindowLongPtr", CharSet = CharSet.Unicode)]
        public static extern long GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int WS_CHILDWINDOW = 0x40000000;
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("Dwmapi.dll", SetLastError = true)]
        public static extern HRESULT DwmGetWindowAttribute(IntPtr hwnd, int dwAttributeToGet, ref int pvAttributeValue, int cbAttribute);

        public enum DWMWINDOWATTRIBUTE { DWMWA_CLOAKED = 14 }

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : new IntPtr(GetClassLong32(hWnd, nIndex));
        }

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetClassLong", CharSet = CharSet.Unicode)]
        private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetClassLongPtr", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("Shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern HRESULT SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore { HRESULT GetValue([In, MarshalAs(UnmanagedType.Struct)] ref PROPERTYKEY key, [Out, MarshalAs(UnmanagedType.Struct)] out PROPVARIANT pv);}

        public static PROPERTYKEY PKEY_AppUserModel_ID = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

        public struct PROPERTYKEY { public PROPERTYKEY(Guid InputId, uint InputPid) { fmtid = InputId; pid = InputPid; } private Guid fmtid; private uint pid; }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        public struct PROPVARIANT { [FieldOffset(0)] public ushort varType; [FieldOffset(8)] public IntPtr pwszVal; }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

        [DllImport("User32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public const int WM_GETICON = 0x007F;
        public const int ICON_SMALL2 = 2;
        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;
        public const int GCL_HICON = -14;

        #endregion

        public class WindowItem
        {
            public IntPtr Hwnd { get; set; }
            public ImageSource Icon { get; set; }
            public string Title { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            this.SourceInitialized += MainWindow_SourceInitialized;
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            m_hWnd = new WindowInteropHelper(this).Handle;
            this.MaxWidth = SystemParameters.WorkArea.Width;
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _updateTimer.Tick += UpdateOpenWindows;
            _updateTimer.Start();
            UpdateOpenWindows(null, EventArgs.Empty);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
        }

        private void UpdateOpenWindows(object sender, EventArgs e)
        {
            var newItems = new List<WindowItem>();
            GCHandle listHandle = GCHandle.Alloc(newItems);
            try { EnumDesktopWindows(IntPtr.Zero, ListWindows, ref Unsafe.As<GCHandle, IntPtr>(ref listHandle)); }
            finally { listHandle.Free(); }

            var currentItems = DockItemsControl.ItemsSource as List<WindowItem> ?? new List<WindowItem>();

            if (currentItems.Count != newItems.Count || !currentItems.SequenceEqual(newItems, new WindowItemComparer()))
            {
                DockItemsControl.ItemsSource = newItems;
            }
        }

        private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WindowItem item)
            {
                Debug.WriteLine($"[CLICK] Icon '{item.Title}' clicked. HWND: {item.Hwnd}");
                ShowWindow(item.Hwnd, SW_RESTORE);
                SetForegroundWindow(item.Hwnd);
            }
        }

        public bool ListWindows(IntPtr hWnd, ref IntPtr lParam)
        {
            long nStyle = GetWindowLong(hWnd, GWL_STYLE);
            long nExStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            if (!IsWindowVisible(hWnd) || GetWindow(hWnd, GW_OWNER) != IntPtr.Zero || (nExStyle & WS_EX_TOOLWINDOW) != 0) return true;

            int nCloaked = 0;
            DwmGetWindowAttribute(hWnd, (int)DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, ref nCloaked, Marshal.SizeOf(typeof(int)));
            if (nCloaked != 0) return true;

            bool bOK = ((nExStyle & WS_EX_APPWINDOW) != 0) ||
                       (GetWindow(hWnd, GW_OWNER) == IntPtr.Zero && (nExStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) == 0);

            if (bOK && (nStyle & WS_CHILDWINDOW) == 0)
            {
                int nTextLength = GetWindowTextLength(hWnd);
                if (nTextLength++ > 0)
                {
                    var sbText = new StringBuilder(nTextLength);
                    GetWindowText(hWnd, sbText, nTextLength);
                    string sTitle = sbText.ToString();

                    if (hWnd != m_hWnd && !string.IsNullOrEmpty(sTitle))
                    {
                        ImageSource icon = GetLargeIconForWindow(hWnd) ?? GetWindowIcon(hWnd) ?? GetUwpAppIcon(hWnd);
                        var list = (GCHandle.FromIntPtr(lParam).Target as List<WindowItem>);
                        list?.Add(new WindowItem { Hwnd = hWnd, Icon = icon, Title = sTitle });
                    }
                }
            }
            return true;
        }

        private ImageSource GetLargeIconForWindow(IntPtr hWnd, int size = 64)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return null;
                var proc = Process.GetProcessById((int)pid);
                string exePath = proc.MainModule.FileName;
                ExtractIconEx(exePath, 0, out IntPtr hLarge, out _, 1);
                if (hLarge == IntPtr.Zero) return null;
                try { return Imaging.CreateBitmapSourceFromHIcon(hLarge, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(size, size)); }
                finally { DestroyIcon(hLarge); }
            }
            catch { return null; }
        }

        private ImageSource GetWindowIcon(IntPtr hWnd)
        {
            IntPtr hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero);
            if (hIcon == IntPtr.Zero) hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
            if (hIcon == IntPtr.Zero) hIcon = GetClassLongPtr(hWnd, GCL_HICON);
            if (hIcon != IntPtr.Zero) { return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
            return null;
        }

        private ImageSource GetUwpAppIcon(IntPtr hWnd)
        {
            HRESULT hr = SHGetPropertyStoreForWindow(hWnd, ref IID_IPropertyStore, out IPropertyStore pPropertyStore);
            if (hr != HRESULT.S_OK) return null;

            try
            {
                PROPVARIANT propVar = new PROPVARIANT();
                pPropertyStore.GetValue(ref PKEY_AppUserModel_ID, out propVar);
                string sAUMID = Marshal.PtrToStringUni(propVar.pwszVal);
                if (string.IsNullOrEmpty(sAUMID)) return null;

                string[] sParts = sAUMID.Split('!');
                if (sParts.Length < 1) return null;

                var pm = new PackageManager();
                var package = pm.FindPackagesForUser("", sParts[0]).FirstOrDefault();
                if (package == null) return null;

                string manifestPath = Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) return null;

                XDocument manifest = XDocument.Load(manifestPath);
                XNamespace nsManifest = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                XNamespace nsUap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";

                var appElem = manifest.Root?.Descendants(nsManifest + "Application").FirstOrDefault(e => sParts.Length > 1 && (string)e.Attribute("Id") == sParts[1])
                           ?? manifest.Root?.Descendants(nsManifest + "Application").FirstOrDefault();

                if (appElem == null) return null;

                var visual = appElem.Descendants(nsUap + "VisualElements").FirstOrDefault();
                if (visual == null) return null;

                var logoAttributes = new[] { "Square150x150Logo", "Square71x71Logo", "Square44x44Logo" };
                string sIconPath = null;
                foreach (var attr in logoAttributes)
                {
                    sIconPath = (string)visual.Attribute(attr);
                    if (!string.IsNullOrEmpty(sIconPath))
                    {
                        Debug.WriteLine($"[UWP ICON] Found logo attribute '{attr}' for {sAUMID}");
                        break;
                    }
                }

                if (string.IsNullOrEmpty(sIconPath)) return null;

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
            catch (Exception ex)
            {
                Debug.WriteLine($"[UWP ICON] EXCEPTION for HWND {hWnd}: {ex.Message}");
            }
            finally
            {
                Marshal.ReleaseComObject(pPropertyStore);
            }
            return null;
        }

        private static Guid IID_IPropertyStore = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    }

    public class WindowItemComparer : IEqualityComparer<WindowItem>
    {
        public bool Equals(WindowItem x, WindowItem y) => x?.Hwnd == y?.Hwnd;
        public int GetHashCode(WindowItem obj) => obj.Hwnd.GetHashCode();
    }
}