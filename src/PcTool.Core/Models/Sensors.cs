namespace PcTool.Core.Models;

public enum SensorKind { Temperature, Clock, Load, Fan, Power, Voltage, Other }

public enum SensorSource { Cpu, Gpu, Memory, Motherboard, Storage, Other }

public sealed record SensorReading(
    SensorSource Source,
    string Hardware,
    string Name,
    SensorKind Kind,
    float? Value,
    float? Min,
    float? Max)
{
    public string Unit => Kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Clock => "MHz",
        SensorKind.Load => "%",
        SensorKind.Fan => "RPM",
        SensorKind.Power => "W",
        SensorKind.Voltage => "V",
        _ => ""
    };
}
