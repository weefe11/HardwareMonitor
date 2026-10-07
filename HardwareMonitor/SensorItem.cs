using CommunityToolkit.Mvvm.ComponentModel;
using LibreHardwareMonitor.Hardware;

namespace HardwareMonitor;

public partial class SensorItem : ObservableObject
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required MetricKind Kind { get; init; }
    public int? CoreNumber { get; init; }
    public int? ThreadNumber { get; init; }
    public string RawIdentifier { get; private set; } = "";
    public string RawName { get; private set; } = "";
    public SensorType Type { get; private set; }
    public float? Current { get; private set; }
    public float? Minimum { get; private set; }
    public float? Maximum { get; private set; }

    [ObservableProperty] private string _value = "—";
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _rangeText = "Мин.: —; макс.: —";
    private readonly TemperatureAlert _alert = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarningSymbol), nameof(WarningBrush))]
    private TemperatureState _warning;
    public string WarningSymbol => Warning == TemperatureState.Normal ? "" : "!";
    public System.Windows.Media.Brush WarningBrush => TemperatureAlert.Brush(Warning);

    public void Update(SensorReading reading)
    {
        if (RawIdentifier != reading.Identifier && RawIdentifier.Length > 0)
        { ResetRange(); _alert.Reset(); }
        RawIdentifier = reading.Identifier;
        RawName = reading.Name;
        Type = reading.Type;
        Current = SensorFormatting.ValidValue(reading.Value, reading.Type);
        if (Current is float value)
        {
            Minimum = Minimum is float min ? Math.Min(min, value) : value;
            Maximum = Maximum is float max ? Math.Max(max, value) : value;
        }
        Value = SensorFormatting.Metric(Current, Type);
        Warning = _alert.Update(Kind, Current);
        UpdateRangeText();
    }

    public void MarkUnavailable()
    {
        Current = null;
        Value = "—";
        _alert.Reset(); Warning = TemperatureState.Normal;
        UpdateRangeText();
    }

    public void ResetRange()
    {
        Minimum = null;
        Maximum = null;
        UpdateRangeText();
    }

    private void UpdateRangeText() => RangeText =
        $"Мин.: {SensorFormatting.Metric(Minimum, Type)}; макс.: {SensorFormatting.Metric(Maximum, Type)}\nИсточник: {RawName}" +
        (Warning == TemperatureState.Normal ? "" : "\n" + TemperatureAlert.Message(Warning));
}

public static class SensorFormatting
{
    public static string Metric(float? value, SensorType type) => ValidValue(value, type) is float number
        ? type switch
        {
            SensorType.Temperature => $"{number:0} °C",
            SensorType.Load => $"{number:0} %",
            SensorType.Clock when number >= 1000 => $"{number / 1000:0.##} ГГц",
            SensorType.SmallData => $"{number / 1024:0.#} ГБ",
            SensorType.Data => $"{number:0.#} ГБ",
            SensorType.Power => $"{number:0.#} Вт",
            SensorType.Fan => $"{number:0} об/мин",
            _ => Format(number, type)
        } : "—";
    public static float? ValidValue(float? value) => value is float number && float.IsFinite(number) ? number : null;
    public static float? ValidValue(float? value, SensorType type)
    {
        if (ValidValue(value) is not float number) return null;
        return type switch
        {
            SensorType.Load when number < 0 || number > 100 => null,
            SensorType.Clock or SensorType.Fan or SensorType.Power or SensorType.Data or SensorType.SmallData when number < 0 => null,
            SensorType.Temperature when number < -80 || number > 200 => null,
            _ => number
        };
    }
    public static string Format(float? value, SensorType type) => ValidValue(value, type) is float number
        ? $"{number:0.0}{Unit(type)}" : "—";

    private static string Unit(SensorType type) => type switch
    {
        SensorType.Temperature => " °C",
        SensorType.Clock => " МГц",
        SensorType.Load => " %",
        SensorType.Power => " Вт",
        SensorType.Data => " ГБ",
        SensorType.SmallData => " МБ",
        SensorType.Fan => " об/мин",
        SensorType.Voltage => " В",
        SensorType.Throughput => " Б/с",
        _ => ""
    };
}
