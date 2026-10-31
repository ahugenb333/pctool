using System.Text.Json;
using PcTool.Core;

namespace PcTool.Data;

/// <summary>Static reference prices from the bundled file. A live retailer feed would implement <see cref="IPriceSource"/> instead.</summary>
public sealed class JsonPriceSource : IPriceSource
{
    private readonly Dictionary<string, decimal> _prices;

    public string Description { get; }

    public JsonPriceSource(Dictionary<string, decimal> prices, string description)
    {
        _prices = new Dictionary<string, decimal>(prices, StringComparer.OrdinalIgnoreCase);
        Description = description;
    }

    public decimal? GetPriceUsd(string partId) => _prices.TryGetValue(partId, out var p) ? p : null;

    public static JsonPriceSource FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var prices = new Dictionary<string, decimal>();
        if (doc.RootElement.TryGetProperty("prices", out var p))
        {
            foreach (var entry in p.EnumerateObject())
                prices[entry.Name] = entry.Value.GetDecimal();
        }
        return new JsonPriceSource(prices, "Approximate launch MSRP (USD), not live prices");
    }
}
