using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Text.Json.Serialization;
using dockus.Core.Services;
namespace dockus.Core.Models;

public class AppSettings : INotifyPropertyChanged
{
    private static AppSettings? _instance;
    public static AppSettings Current { get; } = new PersistenceService().LoadSettings();

    public event PropertyChangedEventHandler? PropertyChanged;

    private Color _dockBackground = (Color)ColorConverter.ConvertFromString("#FF222222");

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