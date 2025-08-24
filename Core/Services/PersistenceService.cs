using dockus.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace dockus.Core.Services;

public class PersistenceService
{
    private const string PinnedAppsFileName = "pinned_apps.json";
    private const string SettingsFileName = "settings.json";
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public void SavePinnedApps(IEnumerable<WindowItem> pinnedItems)
    {
        var pinnedList = new List<PinnedApp>();
        foreach (var p in pinnedItems)
        {
            pinnedList.Add(new PinnedApp { Type = p.IdentifierType, Identifier = p.Identifier });
        }

        string json = JsonSerializer.Serialize(pinnedList, s_jsonOptions);
        File.WriteAllText(PinnedAppsFileName, json);
    }

    public List<PinnedApp> LoadPinnedApps()
    {
        if (!File.Exists(PinnedAppsFileName))
        {
            return new List<PinnedApp>();
        }

        try
        {
            string json = File.ReadAllText(PinnedAppsFileName);
            var pinnedList = JsonSerializer.Deserialize<List<PinnedApp>>(json);
            return pinnedList ?? new List<PinnedApp>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to load pinned apps: {ex.Message}");
            File.Delete(PinnedAppsFileName);
            return new List<PinnedApp>();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, s_jsonOptions);
            File.WriteAllText(SettingsFileName, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to save settings: {ex.Message}");
        }
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFileName))
        {
            return new AppSettings();
        }
        try
        {
            string json = File.ReadAllText(SettingsFileName);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Failed to load settings: {ex.Message}");
            File.Delete(SettingsFileName);
            return new AppSettings();
        }
    }

}