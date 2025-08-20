using dockus.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace dockus.Core.Services;

public class PersistenceService
{
    private const string PinnedAppsFileName = "pinned_apps.json";
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
}