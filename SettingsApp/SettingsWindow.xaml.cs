using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace dockus.Settings
{
    /// <summary>
    /// Lógica de interacción para SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {


        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;



        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useDark = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
    }
}
