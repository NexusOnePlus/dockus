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
        public required string Identifier { get; set; }
    }

    public class WindowItem : INotifyPropertyChanged
    {
        public IntPtr Hwnd { get; set; }
        public string Identifier { get; set; } = string.Empty;
        public PinnedAppType IdentifierType { get; set; }

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private ImageSource? _icon;
        public ImageSource? Icon
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    #endregion

    public partial class MainWindow : Window, IDropTarget
    {
        private const string PinnedAppsFileName = "pinned_apps.json";
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
        private IntPtr m_hWnd = IntPtr.Zero;
        private readonly DispatcherTimer _updateTimer;
        private readonly DispatcherTimer _hideTimer;

        public ObservableCollection<WindowItem> PinnedItems { get; set; }
        public ObservableCollection<WindowItem> ActiveUnpinnedItems { get; set; }

        #region P/Invoke Definitions

        [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            IntPtr ActivateApplication([In] string appUserModelId, [In] string arguments, [In] int options, [Out] out uint processId);
        }

        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ApplicationActivationManager : IApplicationActivationManager
        {
            [MethodImpl(MethodImplOptions.InternalCall, MethodCodeType = MethodCodeType.Runtime)]
            public extern IntPtr ActivateApplication([In] string appUserModelId, [In] string arguments, [In] int options, [Out] out uint processId);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetValue([In, MarshalAs(UnmanagedType.Struct)] ref PROPERTYKEY key, [Out, MarshalAs(UnmanagedType.Struct)] out PROPVARIANT pv);
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool SetForegroundWindow(IntPtr hWnd);

            [DllImport("user32.dll")]
            internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
            internal const int SW_RESTORE = 9;
            internal const int SW_HIDE = 0;
            internal const int SW_SHOW = 5;

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
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpEnumFunc, ref GCHandle lParam);
            internal delegate bool EnumWindowsProc(IntPtr hWnd, ref GCHandle lParam);

            [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
            internal const int GW_OWNER = 4;

            internal static long GetWindowLong(IntPtr hWnd, int nIndex) => IntPtr.Size == 4 ? GetWindowLong32(hWnd, nIndex) : GetWindowLongPtr64(hWnd, nIndex);

            [DllImport("User32.dll", EntryPoint = "GetWindowLong", CharSet = CharSet.Unicode)]
            private static extern long GetWindowLong32(IntPtr hWnd, int nIndex);

            [DllImport("User32.dll", EntryPoint = "GetWindowLongPtr", CharSet = CharSet.Unicode)]
            private static extern long GetWindowLongPtr64(IntPtr hWnd, int nIndex);

            internal const int GWL_STYLE = -16;
            internal const int GWL_EXSTYLE = -20;
            internal const int WS_VISIBLE = 0x10000000;
            internal const int WS_CHILDWINDOW = 0x40000000;
            internal const int WS_EX_APPWINDOW = 0x00040000;
            internal const int WS_EX_TOOLWINDOW = 0x00000080;
            internal const int WS_EX_NOACTIVATE = 0x08000000;

            [DllImport("Dwmapi.dll", SetLastError = true)]
            internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttributeToGet, ref int pvAttributeValue, int cbAttribute);
            internal enum DWMWINDOWATTRIBUTE { DWMWA_CLOAKED = 14 }

            [DllImport("User32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
            internal const int WM_GETICON = 0x007F;
            internal const int ICON_BIG = 1;
            internal const int ICON_SMALL2 = 2;


            [DllImport("Shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

            [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
            internal static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

            [DllImport("User32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DestroyIcon(IntPtr hIcon);

            [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

            [DllImport("shell32.dll", SetLastError = true)]
            internal static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
            internal const int ABM_SETSTATE = 0x0000000A;
            internal const int ABS_AUTOHIDE = 0x0000001;
            internal const int ABS_ALWAYSONTOP = 0x0000002;
        }

        private static readonly Guid IID_IPropertyStore = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROPERTYKEY { public PROPERTYKEY(Guid id, uint pid) { fmtid = id; this.pid = pid; } private Guid fmtid; private uint pid; }

        [StructLayout(LayoutKind.Explicit)]
        private struct PROPVARIANT { [FieldOffset(0)] public ushort varType; [FieldOffset(8)] public IntPtr pwszVal; }

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        #endregion

        public MainWindow()
        {
            InitializeComponent();
            PinnedItems = new ObservableCollection<WindowItem>();
            ActiveUnpinnedItems = new ObservableCollection<WindowItem>();
            this.DataContext = this;
            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
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
            SavePinnedApps();
            RestoreTaskbar();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
        }

        private void UpdateOpenWindows(object? sender, EventArgs e)
        {
            var activeWindows = new List<WindowItem>();
            var handle = GCHandle.Alloc(activeWindows);
            try
            {
                NativeMethods.EnumDesktopWindows(IntPtr.Zero, ListWindows, ref handle);
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
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

        #region Settings and Taskbar Hiding

        private bool _isBarVisible = true;

        private void ToggleBar_Click(object sender, RoutedEventArgs e)
        {
            if (_isBarVisible)
            {
                _hideTimer.Start();
                _isBarVisible = false;
                if (sender is MenuItem menuItem)
                {
                    menuItem.Header = "Mostrar barra";
                }
            }
            else
            {
                _hideTimer.Stop();
                RestoreTaskbar();
                _isBarVisible = true;
                if (sender is MenuItem menuItem)
                {
                    menuItem.Header = "Ocultar barra";
                }
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

        #region User Interaction (Click, Drag & Drop, Pinning, Settings)

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new dockus.Settings.SettingsWindow
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
        }

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
            if (e.Source is FrameworkElement { DataContext: WindowItem item })
            {
                if (string.IsNullOrEmpty(item.Identifier)) return;

                item.IsPinned = true;
                if (!PinnedItems.Any(p => p.Identifier == item.Identifier))
                {
                    PinnedItems.Add(item);
                }
                ActiveUnpinnedItems.Remove(item);
                SavePinnedApps();
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
                SavePinnedApps();
            }
        }

        #endregion

        #region Persistence and App Launching

        private void SavePinnedApps()
        {
            var pinnedList = PinnedItems.Select(p => new PinnedApp { Type = p.IdentifierType, Identifier = p.Identifier }).ToList();
            string json = JsonSerializer.Serialize(pinnedList, s_jsonOptions);
            File.WriteAllText(PinnedAppsFileName, json);
        }

        private void LoadPinnedApps()
        {
            if (!File.Exists(PinnedAppsFileName)) return;
            try
            {
                string json = File.ReadAllText(PinnedAppsFileName);
                var pinnedList = JsonSerializer.Deserialize<List<PinnedApp>>(json);

                if (pinnedList == null) return;

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
                        item.Icon = GetIconFromAumid(pinned.Identifier, out string? title);
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

        private static void LaunchApp(WindowItem item)
        {
            try
            {
                if (item.IdentifierType == PinnedAppType.Path)
                {
                    Process.Start(new ProcessStartInfo(item.Identifier) { UseShellExecute = true });
                }
                else
                {
                    IApplicationActivationManager activationManager = new ApplicationActivationManager();
                    activationManager.ActivateApplication(item.Identifier, null!, 0, out _);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start the application: {ex.Message}");
            }
        }

        #endregion

        #region Window and Icon Information Resolvers

        private bool ListWindows(IntPtr hWnd, ref GCHandle lParam)
        {
            if (lParam.Target is not List<WindowItem> list) return true;

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

                if (hWnd != m_hWnd && !string.IsNullOrEmpty(sTitle))
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

        private static BitmapSource? GetIconFromPath(string path)
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

        private static ImageSource? GetIconFromAumid(string aumid, out string? title)
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

        private static string? GetAumidForWindow(IntPtr hWnd)
        {
            try
            {
                // CORRECTED: Create local copies to pass by ref
                Guid iid = IID_IPropertyStore;
                if (NativeMethods.SHGetPropertyStoreForWindow(hWnd, ref iid, out IPropertyStore pPropertyStore) == 0)
                {
                    try
                    {
                        // CORRECTED: Create local copies to pass by ref
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


        private static string? GetPathForWindow(IntPtr hWnd)
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

        private static BitmapSource? GetWindowIcon(IntPtr hWnd)
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