using System.Text.Json;
using System.Text.Json.Serialization;
using PcTool.Core.Models;

namespace PcTool.Hardware;

public static class ProfileExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(HardwareProfile profile) => JsonSerializer.Serialize(profile, Options);

    public static HardwareProfile? FromJson(string json) => JsonSerializer.Deserialize<HardwareProfile>(json, Options);
}
