using dockus.Core.Services;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media;
namespace dockus.Core.Models;

public class AppSettings : INotifyPropertyChanged
{
    //private static AppSettings? _instance;

    private bool _autoHideWindowsTaskbar = false;
    public bool AutoHideWindowsTaskbar
    {
        get => _autoHideWindowsTaskbar;
        set
        {
            if (_autoHideWindowsTaskbar != value)
            {
                _autoHideWindowsTaskbar = value;
                OnPropertyChanged();
                new PersistenceService().SaveSettings(this);
            }
        }
    }



    private bool _showSystemInfo = true;
    public bool ShowSystemInfo
    {
        get => _showSystemInfo;
        set
        {
            if (_showSystemInfo != value)
            {   
                _showSystemInfo = value;
                OnPropertyChanged();
                new PersistenceService().SaveSettings(this);
            }
        }
    }
    public static AppSettings Current { get; } = new PersistenceService().LoadSettings();

    public event PropertyChangedEventHandler? PropertyChanged;

    private Color _dockBackground = (Color)ColorConverter.ConvertFromString("#FF000000");

    [JsonIgnore]
    public Color DockBackground
    {
        get => _dockBackground;
        set
        {
            if (_dockBackground != value)
            {
                _dockBackground = value;
                OnPropertyChanged();
            }
        }
    }


    public string DockBackgroundString
    {
        get => _dockBackground.ToString();
        set
        {
            DockBackground = (Color)ColorConverter.ConvertFromString(value);
        }
    }


    public AppSettings() { }

    [JsonIgnore]
    public string AppVersion => $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name == nameof(DockBackground))
        {
            new PersistenceService().SaveSettings(this);
        }
    }
}