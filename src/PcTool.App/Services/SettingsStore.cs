using System.IO;
using System.Text.Json;
using PcTool.Core.Models;

namespace PcTool.App.Services;

public sealed class AppSettings
{
    public UserSystemOverrides System { get; set; } = new();
    public decimal UpgradeBudgetUsd { get; set; } = 600;
    public string UpgradeGoal { get; set; } = "Gaming1440p";
}

public sealed class SettingsStore
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcTool");

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(DataDirectory, "settings.json");

    public AppSettings Current { get; private set; }

    public SettingsStore()
    {
        Current = Load();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, Json));
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Json) ?? new AppSettings();
        }
        catch (JsonException)
        {
        }
        return new AppSettings();
    }
}
