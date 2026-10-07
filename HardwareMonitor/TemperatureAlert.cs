using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;


namespace HardwareMonitor;

public enum TemperatureState { Normal, Elevated, High }

public sealed class TemperatureAlert
{
    private TemperatureState _state;
    private TemperatureState _candidate;
    private int _samples;
    public TemperatureState Update(MetricKind kind, float? value)
    {
        var thresholds = Thresholds(kind);
        if (SensorFormatting.ValidValue(value, LibreHardwareMonitor.Hardware.SensorType.Temperature) == null || thresholds == null) { Reset(); return _state; }
        var (warning, critical) = thresholds.Value;
        var next = value >= critical ? TemperatureState.High : value >= warning ? TemperatureState.Elevated : TemperatureState.Normal;
        if (_state == TemperatureState.High && value >= critical - 3) next = TemperatureState.High;
        else if (_state == TemperatureState.Elevated && value >= warning - 3 && next == TemperatureState.Normal) next = TemperatureState.Elevated;
        if (next == _state) { _samples = 0; return _state; }
        if (next != _candidate) { _candidate = next; _samples = 0; }
        if (++_samples >= 3) { _state = next; _samples = 0; }
        return _state;
    }
    public void Reset() { _state = _candidate = TemperatureState.Normal; _samples = 0; }
    public static (float Warning, float Critical)? Thresholds(MetricKind kind) => kind switch
    {
        MetricKind.CpuTemperature or MetricKind.CoreTemperature => (85, 95),
        MetricKind.GpuTemperature => (80, 90),
        MetricKind.GpuHotSpot or MetricKind.GpuMemoryTemperature => (95, 105),
        MetricKind.StorageTemperature => (60, 70),
        _ => null
    };
    public static Brush Brush(TemperatureState state) => state == TemperatureState.High ? Brushes.OrangeRed : Brushes.Gold;
    public static string Message(TemperatureState state) => state switch
    {
        TemperatureState.Elevated => "Повышенная температура. Проверьте охлаждение и допустимую температуру вашей модели.",
        TemperatureState.High => "Высокая температура. Проверьте нагрузку и охлаждение. Это ориентир, а не диагноз неисправности.",
        _ => ""
    };
}
