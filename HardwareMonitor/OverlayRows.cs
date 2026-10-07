using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Hardware;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;


namespace HardwareMonitor;

public partial class OverlayMetricItem : ObservableObject
{
    public required string Key { get; init; }
    public string Label { get; init; } = "";
    public double CellWidth { get; init; } = 96;
    [ObservableProperty] private bool _separator;
    [ObservableProperty] private string _text = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarningSymbol), nameof(WarningBrush), nameof(WarningHint))]
    private TemperatureState _warning;
    public string WarningSymbol => Warning == TemperatureState.Normal ? "" : "!";
    public Brush WarningBrush => TemperatureAlert.Brush(Warning);
    public string WarningHint => TemperatureAlert.Message(Warning);
}

public partial class OverlayRowItem : ObservableObject
{
    public required string Key { get; init; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameWidth))]
    private string _name = "";
    public bool IsDetail { get; init; }
    public System.Windows.Thickness IndentMargin => new(IsDetail ? 16 : 0, 0, 0, 0);
    public double NameWidth => 72;
    public ObservableCollection<OverlayMetricItem> Metrics { get; } = new();
    public string DisplayValue => string.Join(" | ", Metrics.Select(m => m.Label + m.Text));
}

public static class OverlayRows
{
    private const double RowBudget = 490;
    public static IReadOnlyList<OverlayRowItem> Build(IEnumerable<HardwareItem> hardwareGroups)
    {
        var groups = hardwareGroups.ToList();
        var rows = new List<OverlayRowItem>();
        foreach (var group in groups)
        {
            string name = group.Type switch
            {
                HardwareType.Cpu => "ЦП", HardwareType.Memory => "ОЗУ", HardwareType.Storage => "Диск",
                HardwareType.Motherboard => "Плата", _ when SensorCatalog.IsGpu(group.Type) => "ГП", _ => "Датчики"
            };
            var peers = groups.Where(g => SensorCatalog.IsGpu(group.Type) ? SensorCatalog.IsGpu(g.Type) : g.Type == group.Type).ToList();
            int number = peers.IndexOf(group) + 1;
            if (peers.Count > 1) name += " " + number;
            var selected = group.AllSensors.Where(s => s.IsSelected && s.CoreNumber == null).ToList();
            AddRows(group.Key, name, selected.Where(s => !IsSecondary(s.Kind)).OrderBy(s => Order(s.Kind)).ThenBy(s => s.Key, StringComparer.Ordinal));
            if (group.AllSensors.Any(s => s.IsSelected) && !rows.Any(r => r.Key == group.Key))
                rows.Add(new OverlayRowItem { Key = group.Key, Name = name });

            foreach (var core in group.AllSensors.Where(s => s.IsSelected && s.CoreNumber != null).GroupBy(s => s.CoreNumber).OrderBy(g => g.Key))
                AddRows(group.Key + "::core:" + core.Key, $"Ядро {core.Key}", core.OrderBy(s => Order(s.Kind)).ThenBy(s => s.ThreadNumber), true);
            AddRows(group.Key + "::details", rows.Any(r => r.Key == group.Key || r.Key.StartsWith(group.Key + "::core:", StringComparison.Ordinal)) ? "" : name,
                selected.Where(s => IsSecondary(s.Kind)).OrderBy(s => Order(s.Kind)).ThenBy(s => s.Key, StringComparer.Ordinal), true);
        }
        return rows;

        void AddRows(string key, string name, IEnumerable<SensorItem> sensors, bool detail = false)
        {
            OverlayRowItem? row = null;
            double width = 0;
            int part = 0;
            foreach (var sensor in sensors)
            {
                var metric = Metric(sensor);
                if (row == null || width + metric.CellWidth > RowBudget)
                {
                    row = new OverlayRowItem { Key = part == 0 ? key : key + "::part:" + part, Name = part == 0 ? name : "", IsDetail = detail || part > 0 };
                    rows.Add(row); width = 0; part++;
                }
                if (row.Metrics.Count > 0) row.Metrics[^1].Separator = true;
                row.Metrics.Add(metric); width += metric.CellWidth;
            }
        }
    }

    private static bool IsSecondary(MetricKind kind) => kind is MetricKind.GpuHotSpot or MetricKind.GpuMemoryTemperature or MetricKind.GpuMemoryClock or MetricKind.FanSpeed;

    private static OverlayMetricItem Metric(SensorItem sensor)
    {
        string label = sensor.Kind switch
        {
            MetricKind.CoreLoad when sensor.ThreadNumber is int thread => $"П{thread}: ",
            MetricKind.GpuHotSpot => "Макс. ", MetricKind.GpuMemoryTemperature => "Память ",
            MetricKind.GpuMemoryClock => "Память ", MetricKind.GpuMemoryUsed => "Занято ",
            MetricKind.MemoryUsed or MetricKind.StorageUsed => "Занято ",
            MetricKind.MemoryAvailable or MetricKind.StorageFree => "Свободно ", MetricKind.StorageLoad => "Занято ",
            MetricKind.FanSpeed => sensor.Name.Replace("Вентилятор ", "Вент. ") + " ",
            MetricKind.BoardTemperature => sensor.Name == "Температура сокета" ? "Сокет " : "Плата ", _ => ""
        };
        double width = sensor.Kind switch
        {
            MetricKind.CpuTemperature or MetricKind.CoreTemperature or MetricKind.GpuTemperature or MetricKind.StorageTemperature => 78,
            MetricKind.CpuLoad or MetricKind.GpuLoad or MetricKind.MemoryLoad => 66,
            MetricKind.CoreLoad => sensor.ThreadNumber == null ? 66 : 114,
            MetricKind.GpuMemoryUsed => 150, MetricKind.FanSpeed => 164,
            MetricKind.MemoryAvailable or MetricKind.StorageFree => 166,
            MetricKind.MemoryUsed or MetricKind.StorageUsed => 150,
            MetricKind.GpuMemoryTemperature or MetricKind.GpuMemoryClock => 150,
            MetricKind.GpuHotSpot or MetricKind.BoardTemperature => 142,
            MetricKind.StorageLoad => 128,
            MetricKind.CpuPower or MetricKind.GpuPower => 88, _ => 92
        };
        return new OverlayMetricItem { Key = sensor.Key, Label = label, Text = sensor.Value, CellWidth = width, Warning = sensor.Warning };
    }
    private static int Order(MetricKind kind) => kind switch
    {
        MetricKind.CpuTemperature or MetricKind.GpuTemperature or MetricKind.StorageTemperature or MetricKind.CoreTemperature => 0,
        MetricKind.CpuLoad or MetricKind.GpuLoad or MetricKind.MemoryLoad or MetricKind.CoreLoad => 1,
        MetricKind.CpuClock or MetricKind.GpuClock or MetricKind.CoreClock => 2,
        MetricKind.CpuPower or MetricKind.GpuPower => 3,
        _ => 4 + (int)kind
    };
}
