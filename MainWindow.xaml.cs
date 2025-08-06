using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace dockus
{
    #region Data Models and Enums

    public enum PinnedAppType { Path, Aumid }

    public class PinnedApp
    {
        public PinnedAppType Type { get; set; }
        public string Identifier { get; set; }
    }

    public class WindowItem : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; set; }
        public string Identifier { get; set; }
        public PinnedAppType IdentifierType { get; set; }

        private string _title;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private ImageSource _icon;
        public ImageSource Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        private bool _isPinned;
        public bool IsPinned
        {
            get => _isPinned;
            set { _isPinned = value; OnPropertyChanged(); }
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    #endregion

    public partial class MainWindow : Window, IDropTarget
    {
        private const string PinnedAppsFileName = "pinned_apps.json";
        private IntPtr m_hWnd = IntPtr.Zero;
        private DispatcherTimer _updateTimer;

        public ObservableCollection<WindowItem> PinnedItems { get; set; }
        public ObservableCollection<WindowItem> ActiveUnpinnedItems { get; set; }

        #region P/Invoke Definitions

        [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IApplicationActivationManager
        {
            IntPtr ActivateApplication([In] string appUserModelId, [In] string arguments, [In] int options, [Out] out uint processId);
        }
        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        class ApplicationActivationManager : IApplicationActivationManager
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            public extern IntPtr ActivateApplication([In] string appUserModelId, [In] string arguments, [In] int options, [Out] out uint processId);
        }

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
        public static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpEnumFunc, ref GCHandle lParam);
        public delegate bool EnumWindowsProc(IntPtr hWnd, ref GCHandle lParam);

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        public const int GW_OWNER = 4;

        public static long GetWindowLong(IntPtr hWnd, int nIndex) => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLongPtr64(hWnd, nIndex);

        [DllImport("User32.dll", EntryPoint = "GetWindowLong", CharSet = CharSet.Unicode)]
        public static extern long GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("User32.dll", EntryPoint = "GetWindowLongPtr", CharSet = CharSet.Unicode)]
        public static extern long GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CHILDWINDOW = 0x40000000;
        public const int WS_EX_APPWINDOW = 0x00040000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("Dwmapi.dll", SetLastError = true)]
        public static extern HRESULT DwmGetWindowAttribute(IntPtr hwnd, int dwAttributeToGet, ref int pvAttributeValue, int cbAttribute);
        public enum DWMWINDOWATTRIBUTE { DWMWA_CLOAKED = 14 }

        [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex) => IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : new IntPtr(GetClassLong32(hWnd, nIndex));

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetClassLong", CharSet = CharSet.Unicode)]
        private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetClassLongPtr", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("Shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern HRESULT SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore { HRESULT GetValue([In, MarshalAs(UnmanagedType.Struct)] ref PROPERTYKEY key, [Out, MarshalAs(UnmanagedType.Struct)] out PROPVARIANT pv); }

        public static PROPERTYKEY PKEY_AppUserModel_ID = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        public struct PROPERTYKEY { public PROPERTYKEY(Guid id, uint pid) { fmtid = id; this.pid = pid; } private Guid fmtid; private uint pid; }
        [StructLayout(LayoutKind.Explicit)]
        public struct PROPVARIANT { [FieldOffset(0)] public ushort varType; [FieldOffset(8)] public IntPtr pwszVal; }

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

        [DllImport("User32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
        public const int WM_GETICON = 0x007F;
        public const int ICON_BIG = 1;
        public const int ICON_SMALL2 = 2;
        private static Guid IID_IPropertyStore = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

        #endregion

        public MainWindow()
        {
            InitializeComponent();
            PinnedItems = new ObservableCollection<WindowItem>();
            ActiveUnpinnedItems = new ObservableCollection<WindowItem>();
            this.DataContext = this;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            m_hWnd = new WindowInteropHelper(this).Handle;
            this.MaxWidth = SystemParameters.WorkArea.Width;

            LoadPinnedApps();

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _updateTimer.Tick += UpdateOpenWindows;
            _updateTimer.Start();
            UpdateOpenWindows(null, EventArgs.Empty);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SavePinnedApps();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
        }

        private void UpdateOpenWindows(object sender, EventArgs e)
        {
            var activeWindows = new List<WindowItem>();
            var handle = GCHandle.Alloc(activeWindows);
            try
            {
                EnumDesktopWindows(IntPtr.Zero, ListWindows, ref handle);
            }
            finally
            {
                handle.Free();
            }

            foreach (var pinned in PinnedItems)
            {
                var runningInstance = activeWindows.FirstOrDefault(w => w.Identifier == pinned.Identifier);
                pinned.IsRunning = runningInstance != null;
                if (runningInstance != null)
                {
                    pinned.Hwnd = runningInstance.Hwnd;
                    pinned.Title = runningInstance.Title;
                }
            }

            var newActiveUnpinned = activeWindows.Where(w => !PinnedItems.Any(p => p.Identifier == w.Identifier)).ToList();

            var toRemove = ActiveUnpinnedItems.Where(item => !newActiveUnpinned.Any(w => w.Hwnd == item.Hwnd)).ToList();
            foreach (var item in toRemove) ActiveUnpinnedItems.Remove(item);

            var toAdd = newActiveUnpinned.Where(item => !ActiveUnpinnedItems.Any(w => w.Hwnd == item.Hwnd)).ToList();
            foreach (var item in toAdd) ActiveUnpinnedItems.Add(item);
        }

        #region User Interaction (Click, Drag & Drop, Pinning)

        private void Icon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WindowItem item)
            {
                if (item.IsRunning && item.Hwnd != IntPtr.Zero)
                {
                    ShowWindow(item.Hwnd, SW_RESTORE);
                    SetForegroundWindow(item.Hwnd);
                }
                else if (item.IsPinned)
                {
                    LaunchApp(item);
                }
            }
        }

        void IDropTarget.Drop(IDropInfo dropInfo)
        {
            GongSolutions.Wpf.DragDrop.DragDrop.DefaultDropHandler.Drop(dropInfo);
            SavePinnedApps();
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
            if ((e.Source as FrameworkElement)?.DataContext is WindowItem item)
            {
                if (string.IsNullOrEmpty(item.Identifier)) return;

                item.IsPinned = true;
                if (!PinnedItems.Any(p => p.Identifier == item.Identifier))
                {
                    PinnedItems.Add(item);
                }
                if (ActiveUnpinnedItems.Contains(item))
                {
                    ActiveUnpinnedItems.Remove(item);
                }
                SavePinnedApps();
            }
        }

        private void Unpin_Click(object sender, RoutedEventArgs e)
        {
            if ((e.Source as FrameworkElement)?.DataContext is WindowItem item)
            {
                item.IsPinned = false;
                PinnedItems.Remove(item);

                if (item.IsRunning)
                {
                    ActiveUnpinnedItems.Add(item);
                }
                SavePinnedApps();
            }
        }

        #endregion

        #region Persistence and App Launching

        private void SavePinnedApps()
        {
            var pinnedList = PinnedItems.Select(p => new PinnedApp { Type = p.IdentifierType, Identifier = p.Identifier }).ToList();
            string json = JsonSerializer.Serialize(pinnedList, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PinnedAppsFileName, json);
        }

        private void LoadPinnedApps()
        {
            if (!File.Exists(PinnedAppsFileName)) return;
            try
            {
                string json = File.ReadAllText(PinnedAppsFileName);
                var pinnedList = JsonSerializer.Deserialize<List<PinnedApp>>(json);

                PinnedItems.Clear();
                foreach (var pinned in pinnedList)
                {
                    var item = new WindowItem
                    {
                        IsPinned = true,
                        Identifier = pinned.Identifier,
                        IdentifierType = pinned.Type,
                        IsRunning = false
                    };

                    if (pinned.Type == PinnedAppType.Path)
                    {
                        item.Title = Path.GetFileNameWithoutExtension(pinned.Identifier);
                        item.Icon = GetIconFromPath(pinned.Identifier);
                    }
                    else
                    {
                        item.Icon = GetIconFromAumid(pinned.Identifier, out string title);
                        item.Title = title ?? "App";
                    }
                    PinnedItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] Failed to load pinned apps: {ex.Message}");
                // In case of corruption, delete the file to start fresh next time
                File.Delete(PinnedAppsFileName);
            }
        }

        private void LaunchApp(WindowItem item)
        {
            try
            {
                if (item.IdentifierType == PinnedAppType.Path)
                {
                    Process.Start(new ProcessStartInfo(item.Identifier) { UseShellExecute = true });
                }
                else
                {
                    var activationManager = new ApplicationActivationManager();
                    activationManager.ActivateApplication(item.Identifier, null, 0, out _);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start the application: {ex.Message}");
            }
        }

        #endregion

        #region Window and Icon Information Resolvers

        public bool ListWindows(IntPtr hWnd, ref GCHandle lParam)
        {
            var list = (lParam.Target as List<WindowItem>);

            long nStyle = GetWindowLong(hWnd, GWL_STYLE);
            long nExStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            if (!IsWindowVisible(hWnd) || GetWindow(hWnd, GW_OWNER) != IntPtr.Zero || (nExStyle & WS_EX_TOOLWINDOW) != 0) return true;
            int nCloaked = 0;
            DwmGetWindowAttribute(hWnd, (int)DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, ref nCloaked, Marshal.SizeOf(typeof(int)));
            if (nCloaked != 0) return true;

            if ((nExStyle & WS_EX_APPWINDOW) == 0 && GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;
            if ((nStyle & WS_CHILDWINDOW) != 0) return true;

            int nTextLength = GetWindowTextLength(hWnd);
            if (nTextLength++ > 0)
            {
                var sbText = new StringBuilder(nTextLength);
                GetWindowText(hWnd, sbText, nTextLength);
                string sTitle = sbText.ToString();

                if (hWnd != m_hWnd && !string.IsNullOrEmpty(sTitle))
                {
                    var item = new WindowItem { Hwnd = hWnd, Title = sTitle, IsRunning = true };

                    item.Identifier = GetAumidForWindow(hWnd);
                    if (!string.IsNullOrEmpty(item.Identifier))
                    {
                        item.IdentifierType = PinnedAppType.Aumid;
                        item.Icon = GetIconFromAumid(item.Identifier, out _);
                    }
                    else
                    {
                        item.Identifier = GetPathForWindow(hWnd);
                        if (!string.IsNullOrEmpty(item.Identifier))
                        {
                            item.IdentifierType = PinnedAppType.Path;
                            item.Icon = GetIconFromPath(item.Identifier);
                        }
                    }

                    if (item.Icon == null) item.Icon = GetWindowIcon(hWnd);

                    list?.Add(item);
                }
            }
            return true;
        }

        private ImageSource GetIconFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                ExtractIconEx(path, 0, out IntPtr hLarge, out _, 1);
                if (hLarge == IntPtr.Zero) return null;
                try { return Imaging.CreateBitmapSourceFromHIcon(hLarge, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
                finally { DestroyIcon(hLarge); }
            }
            catch { return null; }
        }

        private ImageSource GetIconFromAumid(string aumid, out string title)
        {
            title = null;
            if (string.IsNullOrEmpty(aumid)) return null;

            try
            {
                string[] sParts = aumid.Split('!');
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
                title = (string)visual?.Attribute("DisplayName");

                var logoAttributes = new[] { "Square150x150Logo", "Square71x71Logo", "Square44x44Logo", "Logo" };
                string sIconPath = null;
                foreach (var attr in logoAttributes)
                {
                    sIconPath = (string)visual?.Attribute(attr);
                    if (!string.IsNullOrEmpty(sIconPath)) break;
                }

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

                string executable = (string)appElem.Attribute("Executable");
                if (!string.IsNullOrEmpty(executable))
                {
                    string exePath = Path.Combine(package.InstalledLocation.Path, executable);
                    return GetIconFromPath(exePath);
                }
            }
            catch { /* Fall */ }
            return null;
        }

        private string GetAumidForWindow(IntPtr hWnd)
        {
            try
            {
                SHGetPropertyStoreForWindow(hWnd, ref IID_IPropertyStore, out IPropertyStore pPropertyStore);
                if (pPropertyStore == null) return null;

                try
                {
                    PROPVARIANT propVar = new PROPVARIANT();
                    pPropertyStore.GetValue(ref PKEY_AppUserModel_ID, out propVar);
                    return Marshal.PtrToStringUni(propVar.pwszVal);
                }
                finally
                {
                    Marshal.ReleaseComObject(pPropertyStore);
                }
            }
            catch { return null; }
        }

        private string GetPathForWindow(IntPtr hWnd)
        {
            try
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0) return null;
                using var proc = Process.GetProcessById((int)pid);
                return proc?.MainModule?.FileName;
            }
            catch { return null; }
        }

        private ImageSource GetWindowIcon(IntPtr hWnd)
        {
            IntPtr hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
            if (hIcon == IntPtr.Zero) hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero);
            if (hIcon != IntPtr.Zero) return Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return null;
        }

        #endregion
    }

    #region Value Converters

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && !b) ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && b) ? Brushes.DodgerBlue : Brushes.Transparent;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class MultiBoolToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] is int count1 && values[1] is int count2)
            {
                if (count1 > 0 && count2 > 0) return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    #endregion
}