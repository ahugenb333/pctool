using System.Text.Json;
using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.Data;

public sealed class JsonGameCatalog : IGameCatalog
{
    private readonly Dictionary<string, GameEntry> _byId;

    public IReadOnlyList<GameEntry> Games { get; }

    public JsonGameCatalog(IReadOnlyList<GameEntry> games)
    {
        Games = games;
        _byId = games.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
    }

    public GameEntry? Get(string id) => _byId.GetValueOrDefault(id);

    private sealed class GameFile
    {
        public List<GameEntry> Games { get; init; } = [];
    }

    public static JsonGameCatalog FromJson(string json)
    {
        var file = JsonSerializer.Deserialize<GameFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new GameFile();
        return new JsonGameCatalog(file.Games.OrderBy(g => g.Name).ToList());
    }
}
