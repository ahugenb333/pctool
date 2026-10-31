using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using PcTool.Core;
using PcTool.Core.Models;

namespace PcTool.Hardware;

/// <summary>
/// Sensor readings via LibreHardwareMonitorLib. GPU sensors work unelevated; CPU and motherboard sensors need
/// admin rights plus the PawnIO driver that LibreHardwareMonitor 0.9.5+ uses for low-level access.
/// </summary>
public sealed class LibreSensorMonitor : ISensorMonitor
{
    private readonly object _gate = new();
    private Computer? _computer;

    public bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static bool IsPawnIoInstalled
    {
        get
        {
            try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; }
            catch (Exception) { return false; }
        }
    }

    public IReadOnlyList<SensorReading> Read()
    {
        lock (_gate)
        {
            _computer ??= Open();
            var readings = new List<SensorReading>();
            foreach (var hardware in _computer.Hardware)
                Collect(hardware, readings);
            return readings;
        }
    }

    private static Computer Open()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
        };
        computer.Open();
        return computer;
    }

    private static void Collect(IHardware hardware, List<SensorReading> into)
    {
        hardware.Update();
        var source = hardware.HardwareType switch
        {
            HardwareType.Cpu => SensorSource.Cpu,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => SensorSource.Gpu,
            HardwareType.Memory => SensorSource.Memory,
            HardwareType.Motherboard or HardwareType.SuperIO => SensorSource.Motherboard,
            HardwareType.Storage => SensorSource.Storage,
            _ => SensorSource.Other,
        };

        foreach (var sensor in hardware.Sensors)
        {
            var kind = sensor.SensorType switch
            {
                SensorType.Temperature => SensorKind.Temperature,
                SensorType.Clock => SensorKind.Clock,
                SensorType.Load => SensorKind.Load,
                SensorType.Fan => SensorKind.Fan,
                SensorType.Power => SensorKind.Power,
                SensorType.Voltage => SensorKind.Voltage,
                _ => SensorKind.Other,
            };
            if (kind == SensorKind.Other) continue;
            into.Add(new SensorReading(source, hardware.Name, sensor.Name, kind, sensor.Value, sensor.Min, sensor.Max));
        }

        foreach (var sub in hardware.SubHardware)
            Collect(sub, into);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _computer?.Close();
            _computer = null;
        }
    }
}
