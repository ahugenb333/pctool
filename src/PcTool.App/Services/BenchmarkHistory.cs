using System.IO;
using System.Text.Json;
using PcTool.Core.Models;

namespace PcTool.App.Services;

public sealed class BenchmarkHistory
{
    private readonly string _path = Path.Combine(SettingsStore.DataDirectory, "benchmarks.json");

    public List<BenchmarkRun> Runs { get; }

    public BenchmarkHistory()
    {
        Runs = Load();
    }

    public BenchmarkRun? Latest => Runs.OrderByDescending(r => r.StartedAt).FirstOrDefault();

    public void Add(BenchmarkRun run)
    {
        Runs.Add(run);
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        File.WriteAllText(_path, JsonSerializer.Serialize(Runs, SettingsStore.Json));
    }

    private List<BenchmarkRun> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<BenchmarkRun>>(File.ReadAllText(_path), SettingsStore.Json) ?? [];
        }
        catch (JsonException)
        {
        }
        return [];
    }
}
