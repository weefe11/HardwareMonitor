using LibreHardwareMonitor.Hardware;
using System.Text.RegularExpressions;

namespace HardwareMonitor;

public enum MetricKind
{
    CpuTemperature, CpuLoad, CpuPower, CoreLoad, CoreClock, CpuClock,
    GpuTemperature, GpuHotSpot, GpuMemoryTemperature, GpuLoad, GpuClock,
    GpuMemoryClock, GpuPower, GpuMemoryUsed, MemoryLoad, MemoryUsed,
    StorageTemperature, StorageLoad, StorageUsed, BoardTemperature,
    CoreTemperature, MemoryAvailable, StorageFree, FanSpeed
}

public sealed record SensorDefinition(string Key, string Name, MetricKind Kind, SensorReading Reading,
    int? CoreNumber = null, int? ThreadNumber = null);

public static partial class SensorCatalog
{
    [GeneratedRegex(@"^(?:CPU )?Core #(\d+)(?: Thread #(\d+))?(?: \(Effective\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex CorePattern();

    public static bool IsGpu(HardwareType type) => type is HardwareType.GpuIntel or HardwareType.GpuAmd or HardwareType.GpuNvidia;

    public static string GroupName(SensorReading reading) => reading.RootType switch
    {
        HardwareType.Cpu => "Процессор",
        HardwareType.Memory => "Оперативная память",
        HardwareType.Storage => "Накопитель",
        HardwareType.Motherboard => "Материнская плата",
        _ when IsGpu(reading.RootType) => "Видеокарта",
        _ => reading.RootName
    };

    public static IReadOnlyList<SensorDefinition> Build(IEnumerable<SensorReading> sensors)
    {
        var result = new List<SensorDefinition>();
        foreach (var root in sensors.Select(s => s with { Value = SensorFormatting.ValidValue(s.Value, s.Type) }).GroupBy(s => s.RootId))
        {
            var list = root.OrderBy(s => s.Identifier, StringComparer.Ordinal).ToList();
            var type = list[0].RootType;
            // LibreHardwareMonitor exposes virtual memory as a separate /vram device with the same metric names.
            if (type == HardwareType.Memory && root.Key.Equals("/vram", StringComparison.Ordinal)) continue;
            if (type == HardwareType.Cpu)
            {
                string[] temperatureNames = { "Core (Tdie)", "Core (Tctl/Tdie)", "CPU Package", "Package", "Core Max",
                    "CCDs Max (Tdie)", "CPU Cores", "Core (Tctl)", "Core Average" };
                var temperature = Best(list.Where(s => s.Type == SensorType.Temperature && Matches(s.Name, temperatureNames)), temperatureNames);
                // Individual cores are a fallback. Distance to TjMax and SoC must never become the CPU temperature.
                var fallback = list.Where(s => s.Type == SensorType.Temperature && CorePattern().IsMatch(s.Name))
                    .OrderByDescending(s => SensorFormatting.ValidValue(s.Value)).ThenBy(s => s.Identifier, StringComparer.Ordinal).FirstOrDefault();
                if (temperature == null || (!SensorFormatting.ValidValue(temperature.Value).HasValue && SensorFormatting.ValidValue(fallback?.Value).HasValue))
                    temperature = fallback;
                if (temperature != null) Add(MetricKind.CpuTemperature, "Температура", temperature);
                AddBest(MetricKind.CpuLoad, "Общая загрузка", SensorType.Load, "CPU Total", "Total");
                AddBest(MetricKind.CpuPower, "Мощность", SensorType.Power, "CPU Package", "Package", "CPU PPT", "PPT");
                AddBest(MetricKind.CpuClock, "Средняя частота", SensorType.Clock, "Cores (Average)", "Cores (Average Effective)");
                foreach (var core in list.Where(s => s.Type is SensorType.Load or SensorType.Clock or SensorType.Temperature)
                    .Select(s => (Reading: s, Match: CorePattern().Match(s.Name))).Where(p => p.Match.Success)
                    .GroupBy(p => (p.Reading.Type, Core: p.Reading.PhysicalCoreNumber ?? int.Parse(p.Match.Groups[1].Value),
                        Thread: p.Reading.PhysicalThreadNumber ?? (p.Match.Groups[2].Success ? int.Parse(p.Match.Groups[2].Value) : (int?)null)))
                    .OrderBy(g => g.Key.Core).ThenBy(g => g.Key.Thread))
                {
                    var reading = core.OrderBy(p => SensorFormatting.ValidValue(p.Reading.Value).HasValue ? 0 : 1)
                        .ThenBy(p => p.Reading.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
                        .ThenBy(p => p.Reading.Identifier, StringComparer.Ordinal).First().Reading;
                    string name = $"Ядро {core.Key.Core}" + (core.Key.Thread is int thread ? $" · поток {thread}" : "");
                    var kind = core.Key.Type == SensorType.Load ? MetricKind.CoreLoad : core.Key.Type == SensorType.Clock ? MetricKind.CoreClock : MetricKind.CoreTemperature;
                    // Keep existing selection keys when correcting a backend's logical-core labels.
                    var sourceMatch = CorePattern().Match(reading.Name);
                    int sourceCore = int.Parse(sourceMatch.Groups[1].Value);
                    int? sourceThread = sourceMatch.Groups[2].Success ? int.Parse(sourceMatch.Groups[2].Value) : null;
                    string key = $"{root.Key}::{kind}:{sourceCore}:{sourceThread}";
                    result.Add(new SensorDefinition(key, name, kind, reading, core.Key.Core, core.Key.Thread));
                }
                if (!result.Any(s => s.Kind == MetricKind.CpuClock && s.Reading.RootId == root.Key))
                {
                    var clocks = result.Where(s => s.Kind == MetricKind.CoreClock && s.Reading.RootId == root.Key).Select(s => s.Reading).ToList();
                    if (clocks.Count > 0) Add(MetricKind.CpuClock, "Средняя частота", clocks[0] with
                    {
                        Identifier = root.Key + "/calculated/clock-average", Name = "Среднее показаний частоты ядер",
                        Value = clocks.All(s => SensorFormatting.ValidValue(s.Value).HasValue) ? clocks.Average(s => s.Value!.Value) : null
                    });
                }
            }
            else if (IsGpu(type))
            {
                AddBest(MetricKind.GpuTemperature, "Температура", SensorType.Temperature, "GPU Core", "Core", "GPU Temperature");
                AddBest(MetricKind.GpuHotSpot, "Максимальная температура", SensorType.Temperature, "GPU Hot Spot", "GPU Hotspot", "Hot Spot");
                AddBest(MetricKind.GpuMemoryTemperature, "Температура видеопамяти", SensorType.Temperature, "GPU Memory", "GPU Memory Junction", "Memory Junction");
                AddBest(MetricKind.GpuLoad, "Загрузка", SensorType.Load, "GPU Core", "Core", "D3D 3D", "GPU D3D 3D");
                AddBest(MetricKind.GpuClock, "Частота ядра", SensorType.Clock, "GPU Core", "Core");
                AddBest(MetricKind.GpuMemoryClock, "Частота видеопамяти", SensorType.Clock, "GPU Memory", "Memory");
                AddBest(MetricKind.GpuPower, "Мощность", SensorType.Power, "GPU Package", "GPU Power", "GPU Total", "Total", "GPU PPT");
                AddData(MetricKind.GpuMemoryUsed, "Занято видеопамяти", "GPU Memory Used", "D3D Dedicated Memory Used", "GPU Memory Dedicated");
            }
            else if (type == HardwareType.Memory)
            {
                AddBest(MetricKind.MemoryLoad, "Использование", SensorType.Load, "Memory");
                AddData(MetricKind.MemoryUsed, "Занятый объём", "Memory Used");
                AddData(MetricKind.MemoryAvailable, "Доступный объём", "Memory Available", "Available Memory");


            }
            else if (type == HardwareType.Storage)
            {
                var temperature = Best(list.Where(s => s.Type == SensorType.Temperature && Matches(s.Name, new[] { "Temperature", "Composite Temperature" })), new[] { "Composite Temperature", "Temperature" });
                if (temperature != null) Add(MetricKind.StorageTemperature, "Температура", temperature);
                AddBest(MetricKind.StorageLoad, "Занято места", SensorType.Load, "Used Space");
                AddData(MetricKind.StorageUsed, "Занятый объём", "Used Space");
                AddData(MetricKind.StorageFree, "Свободный объём", "Available Space", "Free Space");

            }
            else if (type == HardwareType.Motherboard)
            {
                foreach (var (names, label) in new[]
                {
                    (new[] { "CPU Socket", "CPU" }, "Температура сокета"),
                    (new[] { "Motherboard", "System" }, "Температура платы")
                })
                {
                    var reading = Best(list.Where(s => s.Type == SensorType.Temperature && Matches(s.Name, names)), names);
                    if (reading != null) result.Add(new SensorDefinition(reading.Identifier, label, MetricKind.BoardTemperature, reading));
                }
            }

            int fanNumber = 0;
            foreach (var fan in list.Where(s => s.Type == SensorType.Fan))
                result.Add(new SensorDefinition(fan.Identifier, $"Вентилятор {++fanNumber}", MetricKind.FanSpeed, fan));

            void Add(MetricKind kind, string name, SensorReading reading) => result.Add(new SensorDefinition($"{root.Key}::{kind}", name, kind, reading));
            void AddBest(MetricKind kind, string name, SensorType sensorType, params string[] names)
            {
                var best = Best(list.Where(s => s.Type == sensorType && Matches(s.Name, names)), names);
                if (best != null) Add(kind, name, best);
            }
            void AddData(MetricKind kind, string name, params string[] names)
            {
                var best = Best(list.Where(s => (s.Type is SensorType.Data or SensorType.SmallData) && Matches(s.Name, names)), names);
                if (best != null) Add(kind, name, best);
            }
        }
        return result;
    }

    private static bool Matches(string name, string[] names) => names.Contains(name, StringComparer.OrdinalIgnoreCase);
    private static SensorReading? Best(IEnumerable<SensorReading> readings, string[] names) => readings
        .OrderBy(s => SensorFormatting.ValidValue(s.Value).HasValue ? 0 : 1)
        .ThenBy(s => { int index = Array.FindIndex(names, n => n.Equals(s.Name, StringComparison.OrdinalIgnoreCase)); return index < 0 ? int.MaxValue : index; })
        .ThenBy(s => s.Identifier, StringComparer.Ordinal).FirstOrDefault();
}
